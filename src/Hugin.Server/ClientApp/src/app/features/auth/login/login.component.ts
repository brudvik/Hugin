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
      background: #1e1e1e;
    }

    .login-card {
      width: 100%;
      max-width: 400px;
      background: #252526;
      border: 1px solid #3c3c3c;
      border-radius: 6px;
      padding: 2rem;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
    }

    .login-header {
      text-align: center;
      margin-bottom: 1.5rem;
    }

    .login-logo {
      width: 72px;
      height: 72px;
      margin: 0 auto 1rem;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .login-logo img {
      width: 64px;
      height: 64px;
      object-fit: contain;
    }

    .login-header h1 {
      font-size: 1.5rem;
      font-weight: 400;
      margin-bottom: 0.5rem;
      color: #e7e7e7;
    }

    .text-muted {
      color: #858585 !important;
    }

    .login-form {
      margin-bottom: 1.5rem;
    }

    .login-footer {
      text-align: center;
      padding-top: 1rem;
      border-top: 1px solid #3c3c3c;
    }

    .input-group-text {
      background: #3c3c3c;
      border-color: #3c3c3c;
      color: #858585;
      border-radius: 2px;
    }

    .form-control {
      background: #3c3c3c;
      border-color: #3c3c3c;
      color: #cccccc;
      border-radius: 2px;
    }

    .form-control:focus {
      background: #3c3c3c;
      border-color: #007fd4;
      color: #e7e7e7;
      box-shadow: none;
      outline: 1px solid #007fd4;
      outline-offset: -1px;
    }

    .form-control::placeholder {
      color: #858585;
    }

    .btn-primary {
      background-color: #0e639c;
      border-color: #0e639c;
      border-radius: 2px;
    }

    .btn-primary:hover {
      background-color: #1177bb;
      border-color: #1177bb;
    }

    .btn-outline-secondary {
      color: #858585;
      border-color: #3c3c3c;
      border-radius: 2px;
    }

    .btn-outline-secondary:hover {
      background-color: #3a3d41;
      border-color: #3a3d41;
      color: #cccccc;
    }

    .form-label {
      color: #cccccc;
      font-size: 0.8125rem;
      font-weight: 400;
    }

    .alert-danger {
      background-color: rgba(241, 76, 76, 0.1);
      border: none;
      border-left: 3px solid #f14c4c;
      color: #f14c4c;
      border-radius: 0;
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

    this.authService.login({ username, password }).subscribe({
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
