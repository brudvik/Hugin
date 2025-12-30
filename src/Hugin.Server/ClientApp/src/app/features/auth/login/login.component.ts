// Hugin Admin Panel - Login Component
import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, ActivatedRoute } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '@core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="login-container">
      <div class="login-card">
        <div class="login-header">
          <div class="login-logo">
            <img src="hugin-logo.png" alt="Hugin Logo" style="width: 64px; height: 64px;">
          </div>
          <h1>Hugin Admin</h1>
          <p class="text-muted">Logg inn for å administrere IRC-serveren</p>
        </div>

        <form [formGroup]="loginForm" (ngSubmit)="onSubmit()" class="login-form">
          @if (error()) {
            <div class="alert alert-danger d-flex align-items-center" role="alert">
              <i class="fas fa-exclamation-circle me-2"></i>
              <span>{{ error() }}</span>
            </div>
          }

          <div class="mb-3">
            <label for="username" class="form-label">Brukernavn</label>
            <div class="input-group">
              <span class="input-group-text">
                <i class="fas fa-user"></i>
              </span>
              <input 
                type="text" 
                id="username"
                class="form-control" 
                formControlName="username"
                placeholder="Skriv inn brukernavn"
                [class.is-invalid]="isFieldInvalid('username')"
                autocomplete="username"
              >
            </div>
            @if (isFieldInvalid('username')) {
              <div class="invalid-feedback d-block">
                Brukernavn er påkrevd
              </div>
            }
          </div>

          <div class="mb-4">
            <label for="password" class="form-label">Passord</label>
            <div class="input-group">
              <span class="input-group-text">
                <i class="fas fa-lock"></i>
              </span>
              <input 
                [type]="showPassword() ? 'text' : 'password'"
                id="password"
                class="form-control" 
                formControlName="password"
                placeholder="Skriv inn passord"
                [class.is-invalid]="isFieldInvalid('password')"
                autocomplete="current-password"
              >
              <button 
                type="button" 
                class="btn btn-outline-secondary"
                (click)="togglePassword()">
                <i class="fas" [class.fa-eye]="!showPassword()" [class.fa-eye-slash]="showPassword()"></i>
              </button>
            </div>
            @if (isFieldInvalid('password')) {
              <div class="invalid-feedback d-block">
                Passord er påkrevd
              </div>
            }
          </div>

          <button 
            type="submit" 
            class="btn btn-primary w-100 py-2"
            [disabled]="loading()">
            @if (loading()) {
              <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
              Logger inn...
            } @else {
              <i class="fas fa-sign-in-alt me-2"></i>
              Logg inn
            }
          </button>
        </form>

        <div class="login-footer">
          <small class="text-muted">
            <i class="fas fa-shield-alt me-1"></i>
            Sikret med TLS og JWT-autentisering
          </small>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .login-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 2rem;
      background: linear-gradient(135deg, var(--bg-dark) 0%, #1a1d21 100%);
    }

    .login-card {
      width: 100%;
      max-width: 400px;
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 1rem;
      padding: 2.5rem;
    }

    .login-header {
      text-align: center;
      margin-bottom: 2rem;
    }

    .login-logo {
      width: 80px;
      height: 80px;
      margin: 0 auto 1rem;
      background: linear-gradient(135deg, var(--bs-primary), var(--bs-info));
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 2.5rem;
      color: white;
    }

    .login-header h1 {
      font-size: 1.75rem;
      font-weight: 700;
      margin-bottom: 0.5rem;
    }

    .login-form {
      margin-bottom: 1.5rem;
    }

    .login-footer {
      text-align: center;
      padding-top: 1rem;
      border-top: 1px solid var(--bs-border-color);
    }

    .input-group-text {
      background: var(--bs-dark);
      border-color: var(--bs-border-color);
      color: var(--bs-gray-400);
    }

    .form-control {
      background: var(--bs-dark);
      border-color: var(--bs-border-color);
      color: var(--bs-light);
    }

    .form-control:focus {
      background: var(--bs-dark);
      border-color: var(--bs-primary);
      color: var(--bs-light);
      box-shadow: 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25);
    }

    .form-control::placeholder {
      color: var(--bs-gray-600);
    }
  `]
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  loginForm: FormGroup;
  loading = signal(false);
  error = signal<string | null>(null);
  showPassword = signal(false);

  constructor() {
    this.loginForm = this.fb.group({
      username: ['', [Validators.required]],
      password: ['', [Validators.required]]
    });
  }

  isFieldInvalid(field: string): boolean {
    const control = this.loginForm.get(field);
    return !!(control && control.invalid && control.touched);
  }

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

  onSubmit(): void {
    if (this.loginForm.invalid) {
      Object.keys(this.loginForm.controls).forEach(key => {
        this.loginForm.get(key)?.markAsTouched();
      });
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    const { username, password } = this.loginForm.value;

    this.authService.login(username, password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading.set(false);
        if (err.status === 401) {
          this.error.set('Ugyldig brukernavn eller passord');
        } else if (err.status === 429) {
          this.error.set('For mange forsøk. Vennligst vent litt.');
        } else {
          this.error.set('Kunne ikke koble til serveren. Prøv igjen senere.');
        }
      }
    });
  }
}
