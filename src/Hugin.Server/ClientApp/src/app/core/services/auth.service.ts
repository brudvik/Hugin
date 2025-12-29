// Hugin Admin Panel - Auth Service
import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap, catchError, of, map } from 'rxjs';
import { environment } from '@env/environment';
import { ApiResponse, LoginRequest, LoginResponse, AdminUser } from '../models/api.models';

const TOKEN_KEY = 'hugin_token';
const REFRESH_TOKEN_KEY = 'hugin_refresh_token';
const USER_KEY = 'hugin_user';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private currentUser = signal<AdminUser | null>(null);
  private tokenValue = signal<string | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => !!this.tokenValue());
  readonly isAdmin = computed(() => this.currentUser()?.roles.includes('Admin') ?? false);

  constructor() {
    this.loadFromStorage();
  }

  private loadFromStorage(): void {
    const token = localStorage.getItem(TOKEN_KEY);
    const userJson = localStorage.getItem(USER_KEY);

    if (token && userJson) {
      this.tokenValue.set(token);
      try {
        this.currentUser.set(JSON.parse(userJson));
      } catch {
        this.clearAuth();
      }
    }
  }

  checkAuth(): void {
    if (this.tokenValue()) {
      this.getProfile().subscribe({
        error: () => this.clearAuth()
      });
    }
  }

  getToken(): string | null {
    return this.tokenValue();
  }

  login(credentials: LoginRequest): Observable<boolean> {
    return this.http.post<ApiResponse<LoginResponse>>(
      `${environment.apiUrl}/auth/login`,
      credentials
    ).pipe(
      tap(response => {
        if (response.success && response.data) {
          this.setAuth(response.data);
        }
      }),
      map(response => response.success),
      catchError(() => of(false))
    );
  }

  logout(): void {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    
    if (refreshToken) {
      this.http.post(`${environment.apiUrl}/auth/logout`, { refreshToken })
        .subscribe();
    }

    this.clearAuth();
    this.router.navigate(['/login']);
  }

  refreshToken(): Observable<boolean> {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    
    if (!refreshToken) {
      return of(false);
    }

    return this.http.post<ApiResponse<LoginResponse>>(
      `${environment.apiUrl}/auth/refresh`,
      { refreshToken }
    ).pipe(
      tap(response => {
        if (response.success && response.data) {
          this.setAuth(response.data);
        }
      }),
      map(response => response.success),
      catchError(() => {
        this.clearAuth();
        return of(false);
      })
    );
  }

  getProfile(): Observable<AdminUser | null> {
    return this.http.get<ApiResponse<AdminUser>>(
      `${environment.apiUrl}/auth/me`
    ).pipe(
      tap(response => {
        if (response.success && response.data) {
          this.currentUser.set(response.data);
          localStorage.setItem(USER_KEY, JSON.stringify(response.data));
        }
      }),
      map(response => response.data ?? null),
      catchError(() => of(null))
    );
  }

  private setAuth(response: LoginResponse): void {
    localStorage.setItem(TOKEN_KEY, response.token);
    if (response.refreshToken) {
      localStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
    }
    
    const user: AdminUser = {
      id: '',
      username: response.displayName,
      displayName: response.displayName,
      email: '',
      roles: response.roles,
      createdAt: new Date().toISOString()
    };
    
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this.tokenValue.set(response.token);
    this.currentUser.set(user);
  }

  private clearAuth(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this.tokenValue.set(null);
    this.currentUser.set(null);
  }
}
