// Hugin Admin Panel - Setup Service
import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '@env/environment';
import { 
  ApiResponse, 
  SetupState, 
  SetupRequired,
  SetupServerRequest,
  SetupTlsRequest,
  TlsSetupResult,
  SetupDatabaseRequest,
  DatabaseTestResult,
  SetupAdminRequest
} from '../models/api.models';

@Injectable({
  providedIn: 'root'
})
export class SetupService {
  private http = inject(HttpClient);

  private setupState = signal<SetupState | null>(null);
  private setupRequired = signal<boolean | null>(null);

  readonly state = this.setupState.asReadonly();
  readonly isSetupRequired = this.setupRequired.asReadonly();

  checkSetupRequired(): Observable<SetupRequired> {
    return this.http.get<ApiResponse<SetupRequired>>(
      `${environment.apiUrl}/setup/required`
    ).pipe(
      map(response => response.data!),
      tap(data => this.setupRequired.set(data.setupRequired))
    );
  }

  getState(): Observable<SetupState> {
    return this.http.get<ApiResponse<SetupState>>(
      `${environment.apiUrl}/setup/state`
    ).pipe(
      map(response => response.data!),
      tap(state => this.setupState.set(state))
    );
  }

  configureServer(config: SetupServerRequest): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(
      `${environment.apiUrl}/setup/server`,
      config
    ).pipe(
      map(response => response.success)
    );
  }

  configureTls(config: SetupTlsRequest): Observable<TlsSetupResult> {
    return this.http.post<ApiResponse<TlsSetupResult>>(
      `${environment.apiUrl}/setup/tls`,
      config
    ).pipe(
      map(response => response.data!)
    );
  }

  testDatabase(config: SetupDatabaseRequest): Observable<DatabaseTestResult> {
    return this.http.post<ApiResponse<DatabaseTestResult>>(
      `${environment.apiUrl}/setup/database/test`,
      config
    ).pipe(
      map(response => response.data!)
    );
  }

  configureDatabase(config: SetupDatabaseRequest): Observable<DatabaseTestResult> {
    return this.http.post<ApiResponse<DatabaseTestResult>>(
      `${environment.apiUrl}/setup/database`,
      config
    ).pipe(
      map(response => response.data!)
    );
  }

  createAdmin(admin: SetupAdminRequest): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(
      `${environment.apiUrl}/setup/admin`,
      admin
    ).pipe(
      map(response => response.success)
    );
  }

  completeSetup(): Observable<boolean> {
    return this.http.post<ApiResponse<void>>(
      `${environment.apiUrl}/setup/complete`,
      {}
    ).pipe(
      map(response => response.success),
      tap(success => {
        if (success) {
          this.setupRequired.set(false);
        }
      })
    );
  }
}
