// Hugin Admin Panel - Setup Wizard Component
import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { SetupService } from '@core/services/setup.service';

type SetupStep = 'welcome' | 'server' | 'tls' | 'database' | 'admin' | 'complete';

interface StepInfo {
  id: SetupStep;
  title: string;
  description: string;
  icon: string;
}

@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="setup-container">
      <div class="setup-card">
        <!-- Progress Header -->
        <div class="setup-header">
          <div class="setup-logo">
            <img src="hugin-logo.png" alt="Hugin Logo" style="width: 64px; height: 64px;">
          </div>
          <h1>Hugin Oppsett</h1>
          <p class="text-muted">Konfigurer IRC-serveren din i noen enkle steg</p>

          <!-- Progress Steps -->
          <div class="setup-progress">
            @for (step of steps; track step.id; let i = $index) {
              <div class="progress-step" 
                   [class.active]="currentStepIndex() === i"
                   [class.completed]="currentStepIndex() > i">
                <div class="step-number">
                  @if (currentStepIndex() > i) {
                    <i class="fas fa-check"></i>
                  } @else {
                    {{ i + 1 }}
                  }
                </div>
                <div class="step-label d-none d-md-block">{{ step.title }}</div>
              </div>
              @if (i < steps.length - 1) {
                <div class="progress-line" [class.completed]="currentStepIndex() > i"></div>
              }
            }
          </div>
        </div>

        <!-- Step Content -->
        <div class="setup-content">
          @if (loading()) {
            <div class="text-center py-5">
              <div class="spinner-border text-primary mb-3" role="status">
                <span class="visually-hidden">Laster...</span>
              </div>
              <p class="text-muted">{{ loadingMessage() }}</p>
            </div>
          } @else {
            @switch (currentStep()) {
              @case ('welcome') {
                <div class="step-welcome text-center">
                  <div class="step-icon mb-4">
                    <i class="fas fa-rocket"></i>
                  </div>
                  <h2>Velkommen til Hugin!</h2>
                  <p class="lead text-muted mb-4">
                    La oss konfigurere IRC-serveren din. Dette tar bare noen minutter.
                  </p>
                  <div class="feature-list text-start mb-4">
                    <div class="feature-item">
                      <i class="fas fa-shield-alt text-success me-2"></i>
                      Sikker TLS-kryptering
                    </div>
                    <div class="feature-item">
                      <i class="fas fa-database text-info me-2"></i>
                      PostgreSQL-database
                    </div>
                    <div class="feature-item">
                      <i class="fas fa-users text-warning me-2"></i>
                      Brukeradministrasjon
                    </div>
                    <div class="feature-item">
                      <i class="fas fa-plug text-primary me-2"></i>
                      Utvidbar arkitektur
                    </div>
                  </div>
                </div>
              }

              @case ('server') {
                <div class="step-server">
                  <h2><i class="fas fa-server me-2"></i>Server-konfigurasjon</h2>
                  <p class="text-muted mb-4">Grunnleggende innstillinger for IRC-serveren</p>

                  <form [formGroup]="serverForm">
                    <div class="row">
                      <div class="col-md-6 mb-3">
                        <label class="form-label">Servernavn</label>
                        <input type="text" class="form-control" formControlName="serverName"
                               placeholder="irc.example.com">
                        <small class="form-text text-muted">Fullt kvalifisert domenenavn for serveren</small>
                      </div>
                      <div class="col-md-6 mb-3">
                        <label class="form-label">Nettverksnavn</label>
                        <input type="text" class="form-control" formControlName="networkName"
                               placeholder="MyNetwork">
                        <small class="form-text text-muted">Navn på IRC-nettverket</small>
                      </div>
                    </div>

                    <div class="row">
                      <div class="col-md-6 mb-3">
                        <label class="form-label">TLS-port</label>
                        <input type="number" class="form-control" formControlName="tlsPort"
                               placeholder="6697">
                        <small class="form-text text-muted">Standard TLS-port er 6697</small>
                      </div>
                      <div class="col-md-6 mb-3">
                        <label class="form-label">Maks tilkoblinger</label>
                        <input type="number" class="form-control" formControlName="maxConnections"
                               placeholder="1000">
                      </div>
                    </div>

                    <div class="mb-3">
                      <label class="form-label">MOTD (Message of the Day)</label>
                      <textarea class="form-control" formControlName="motd" rows="4"
                                placeholder="Velkommen til vår IRC-server!"></textarea>
                    </div>
                  </form>
                </div>
              }

              @case ('tls') {
                <div class="step-tls">
                  <h2><i class="fas fa-lock me-2"></i>TLS-sertifikat</h2>
                  <p class="text-muted mb-4">Konfigurer sikker tilkobling med TLS</p>

                  <form [formGroup]="tlsForm">
                    <div class="mb-3">
                      <label class="form-label">Sertifikat-type</label>
                      <select class="form-select" formControlName="certificateType">
                        <option value="generate">Generer selv-signert sertifikat</option>
                        <option value="existing">Bruk eksisterende sertifikat</option>
                        <option value="letsencrypt">Let's Encrypt (kommer snart)</option>
                      </select>
                    </div>

                    @if (tlsForm.get('certificateType')?.value === 'existing') {
                      <div class="mb-3">
                        <label class="form-label">Sertifikat-fil (.pfx)</label>
                        <input type="text" class="form-control" formControlName="certificatePath"
                               placeholder="/path/to/certificate.pfx">
                      </div>
                      <div class="mb-3">
                        <label class="form-label">Sertifikat-passord</label>
                        <input type="password" class="form-control" formControlName="certificatePassword"
                               placeholder="Passord for sertifikatet">
                      </div>
                    }

                    @if (tlsForm.get('certificateType')?.value === 'generate') {
                      <div class="alert alert-info">
                        <i class="fas fa-info-circle me-2"></i>
                        Et selv-signert sertifikat vil bli generert automatisk. 
                        For produksjon anbefales det å bruke et gyldig sertifikat fra en CA.
                      </div>
                    }

                    <div class="form-check mb-3">
                      <input type="checkbox" class="form-check-input" formControlName="requireTls" id="requireTls">
                      <label class="form-check-label" for="requireTls">
                        Krev TLS for alle tilkoblinger (anbefalt)
                      </label>
                    </div>
                  </form>
                </div>
              }

              @case ('database') {
                <div class="step-database">
                  <h2><i class="fas fa-database me-2"></i>Database</h2>
                  <p class="text-muted mb-4">Koble til PostgreSQL-databasen</p>

                  <form [formGroup]="databaseForm">
                    <div class="row">
                      <div class="col-md-8 mb-3">
                        <label class="form-label">Vert</label>
                        <input type="text" class="form-control" formControlName="host"
                               placeholder="localhost">
                      </div>
                      <div class="col-md-4 mb-3">
                        <label class="form-label">Port</label>
                        <input type="number" class="form-control" formControlName="port"
                               placeholder="5432">
                      </div>
                    </div>

                    <div class="row">
                      <div class="col-md-6 mb-3">
                        <label class="form-label">Brukernavn</label>
                        <input type="text" class="form-control" formControlName="username"
                               placeholder="postgres">
                      </div>
                      <div class="col-md-6 mb-3">
                        <label class="form-label">Passord</label>
                        <input type="password" class="form-control" formControlName="password"
                               placeholder="Database-passord">
                      </div>
                    </div>

                    <div class="mb-3">
                      <label class="form-label">Databasenavn</label>
                      <input type="text" class="form-control" formControlName="database"
                             placeholder="hugin">
                    </div>

                    @if (dbTestResult()) {
                      <div class="alert" 
                           [class.alert-success]="dbTestResult()?.success"
                           [class.alert-danger]="!dbTestResult()?.success">
                        <i class="fas me-2" 
                           [class.fa-check-circle]="dbTestResult()?.success"
                           [class.fa-times-circle]="!dbTestResult()?.success"></i>
                        {{ dbTestResult()?.message }}
                      </div>
                    }

                    <button type="button" class="btn btn-outline-primary" 
                            (click)="testDatabase()" [disabled]="testingDb()">
                      @if (testingDb()) {
                        <span class="spinner-border spinner-border-sm me-2"></span>
                        Tester...
                      } @else {
                        <i class="fas fa-plug me-2"></i>
                        Test tilkobling
                      }
                    </button>
                  </form>
                </div>
              }

              @case ('admin') {
                <div class="step-admin">
                  <h2><i class="fas fa-user-shield me-2"></i>Administrator</h2>
                  <p class="text-muted mb-4">Opprett første administratorkonto</p>

                  <form [formGroup]="adminForm">
                    <div class="mb-3">
                      <label class="form-label">Brukernavn</label>
                      <input type="text" class="form-control" formControlName="username"
                             placeholder="admin">
                      <small class="form-text text-muted">
                        Brukernavnet du vil logge inn med i admin-panelet
                      </small>
                    </div>

                    <div class="mb-3">
                      <label class="form-label">E-post</label>
                      <input type="email" class="form-control" formControlName="email"
                             placeholder="admin@example.com">
                    </div>

                    <div class="mb-3">
                      <label class="form-label">Passord</label>
                      <input type="password" class="form-control" formControlName="password"
                             placeholder="Velg et sterkt passord">
                      <div class="password-strength mt-2">
                        <div class="progress" style="height: 4px;">
                          <div class="progress-bar" 
                               [class.bg-danger]="passwordStrength() < 2"
                               [class.bg-warning]="passwordStrength() === 2"
                               [class.bg-success]="passwordStrength() > 2"
                               [style.width.%]="passwordStrength() * 25"></div>
                        </div>
                        <small class="text-muted">{{ passwordStrengthText() }}</small>
                      </div>
                    </div>

                    <div class="mb-3">
                      <label class="form-label">Bekreft passord</label>
                      <input type="password" class="form-control" formControlName="confirmPassword"
                             placeholder="Gjenta passordet"
                             [class.is-invalid]="adminForm.errors?.['passwordMismatch'] && adminForm.get('confirmPassword')?.touched">
                      @if (adminForm.errors?.['passwordMismatch'] && adminForm.get('confirmPassword')?.touched) {
                        <div class="invalid-feedback">
                          Passordene samsvarer ikke
                        </div>
                      }
                    </div>
                  </form>
                </div>
              }

              @case ('complete') {
                <div class="step-complete text-center">
                  <div class="step-icon success mb-4">
                    <i class="fas fa-check"></i>
                  </div>
                  <h2>Oppsettet er fullført!</h2>
                  <p class="lead text-muted mb-4">
                    IRC-serveren din er nå konfigurert og klar til bruk.
                  </p>
                  
                  <div class="summary-card mb-4">
                    <h5>Oppsummering</h5>
                    <dl class="row mb-0">
                      <dt class="col-sm-4">Server:</dt>
                      <dd class="col-sm-8">{{ serverForm.get('serverName')?.value }}</dd>
                      <dt class="col-sm-4">Nettverk:</dt>
                      <dd class="col-sm-8">{{ serverForm.get('networkName')?.value }}</dd>
                      <dt class="col-sm-4">TLS-port:</dt>
                      <dd class="col-sm-8">{{ serverForm.get('tlsPort')?.value }}</dd>
                      <dt class="col-sm-4">Admin:</dt>
                      <dd class="col-sm-8">{{ adminForm.get('username')?.value }}</dd>
                    </dl>
                  </div>

                  <div class="alert alert-success">
                    <i class="fas fa-info-circle me-2"></i>
                    Du vil nå bli videresendt til innlogging. Bruk administratorkontoen du nettopp opprettet.
                  </div>
                </div>
              }
            }
          }

          @if (error()) {
            <div class="alert alert-danger mt-3">
              <i class="fas fa-exclamation-circle me-2"></i>
              {{ error() }}
            </div>
          }
        </div>

        <!-- Navigation Buttons -->
        <div class="setup-footer">
          @if (currentStep() !== 'complete') {
            <button type="button" class="btn btn-outline-secondary" 
                    (click)="previousStep()" 
                    [disabled]="currentStep() === 'welcome' || loading()">
              <i class="fas fa-arrow-left me-2"></i>
              Tilbake
            </button>
            <button type="button" class="btn btn-primary" 
                    (click)="nextStep()" 
                    [disabled]="!canProceed() || loading()">
              @if (currentStep() === 'admin') {
                <i class="fas fa-check me-2"></i>
                Fullfør oppsett
              } @else {
                Neste
                <i class="fas fa-arrow-right ms-2"></i>
              }
            </button>
          } @else {
            <button type="button" class="btn btn-primary btn-lg" (click)="goToLogin()">
              <i class="fas fa-sign-in-alt me-2"></i>
              Gå til innlogging
            </button>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .setup-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 2rem;
      background: linear-gradient(135deg, var(--bg-dark) 0%, #1a1d21 100%);
    }

    .setup-card {
      width: 100%;
      max-width: 700px;
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 1rem;
      overflow: hidden;
    }

    .setup-header {
      text-align: center;
      padding: 2rem;
      background: rgba(0, 0, 0, 0.2);
      border-bottom: 1px solid var(--bs-border-color);
    }

    .setup-logo {
      width: 60px;
      height: 60px;
      margin: 0 auto 1rem;
      background: linear-gradient(135deg, var(--bs-primary), var(--bs-info));
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 1.75rem;
      color: white;
    }

    .setup-header h1 {
      font-size: 1.5rem;
      font-weight: 700;
      margin-bottom: 0.25rem;
    }

    .setup-progress {
      display: flex;
      align-items: center;
      justify-content: center;
      margin-top: 2rem;
      gap: 0;
    }

    .progress-step {
      display: flex;
      flex-direction: column;
      align-items: center;
      position: relative;
    }

    .step-number {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: var(--bs-dark);
      border: 2px solid var(--bs-border-color);
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 0.875rem;
      transition: all 0.3s ease;
    }

    .progress-step.active .step-number {
      background: var(--bs-primary);
      border-color: var(--bs-primary);
      color: white;
    }

    .progress-step.completed .step-number {
      background: var(--bs-success);
      border-color: var(--bs-success);
      color: white;
    }

    .step-label {
      font-size: 0.75rem;
      margin-top: 0.5rem;
      color: var(--bs-gray-500);
    }

    .progress-step.active .step-label,
    .progress-step.completed .step-label {
      color: var(--bs-light);
    }

    .progress-line {
      width: 40px;
      height: 2px;
      background: var(--bs-border-color);
      margin: 0 0.5rem;
      margin-bottom: 1.5rem;
      transition: all 0.3s ease;
    }

    .progress-line.completed {
      background: var(--bs-success);
    }

    .setup-content {
      padding: 2rem;
      min-height: 350px;
    }

    .setup-content h2 {
      font-size: 1.25rem;
      margin-bottom: 0.5rem;
    }

    .step-icon {
      width: 100px;
      height: 100px;
      margin: 0 auto;
      background: linear-gradient(135deg, var(--bs-primary), var(--bs-info));
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 3rem;
      color: white;
    }

    .step-icon.success {
      background: linear-gradient(135deg, var(--bs-success), #0d9488);
    }

    .feature-list {
      max-width: 300px;
      margin: 0 auto;
    }

    .feature-item {
      padding: 0.5rem 0;
      font-size: 0.95rem;
    }

    .summary-card {
      background: rgba(0, 0, 0, 0.2);
      border-radius: 0.5rem;
      padding: 1.5rem;
      text-align: left;
    }

    .summary-card h5 {
      margin-bottom: 1rem;
      color: var(--bs-gray-400);
      font-size: 0.875rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .setup-footer {
      padding: 1.5rem 2rem;
      border-top: 1px solid var(--bs-border-color);
      display: flex;
      justify-content: space-between;
      gap: 1rem;
    }

    .setup-footer .btn-primary {
      min-width: 150px;
    }

    .form-control, .form-select {
      background: var(--bs-dark);
      border-color: var(--bs-border-color);
      color: var(--bs-light);
    }

    .form-control:focus, .form-select:focus {
      background: var(--bs-dark);
      border-color: var(--bs-primary);
      color: var(--bs-light);
      box-shadow: 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25);
    }

    .form-text {
      font-size: 0.8rem;
    }
  `]
})
export class SetupComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly setupService = inject(SetupService);
  private readonly router = inject(Router);

  steps: StepInfo[] = [
    { id: 'welcome', title: 'Velkommen', description: 'Kom i gang', icon: 'rocket' },
    { id: 'server', title: 'Server', description: 'Grunnleggende', icon: 'server' },
    { id: 'tls', title: 'TLS', description: 'Sikkerhet', icon: 'lock' },
    { id: 'database', title: 'Database', description: 'Lagring', icon: 'database' },
    { id: 'admin', title: 'Admin', description: 'Administrator', icon: 'user-shield' },
    { id: 'complete', title: 'Ferdig', description: 'Fullført', icon: 'check' }
  ];

  currentStep = signal<SetupStep>('welcome');
  loading = signal(false);
  loadingMessage = signal('');
  error = signal<string | null>(null);
  testingDb = signal(false);
  dbTestResult = signal<{ success: boolean; message: string } | null>(null);

  currentStepIndex = computed(() => this.steps.findIndex(s => s.id === this.currentStep()));

  serverForm: FormGroup;
  tlsForm: FormGroup;
  databaseForm: FormGroup;
  adminForm: FormGroup;

  constructor() {
    this.serverForm = this.fb.group({
      serverName: ['', [Validators.required, Validators.pattern(/^[a-zA-Z0-9][a-zA-Z0-9.-]+[a-zA-Z0-9]$/)]],
      networkName: ['', [Validators.required]],
      tlsPort: [6697, [Validators.required, Validators.min(1), Validators.max(65535)]],
      maxConnections: [1000, [Validators.required, Validators.min(1)]],
      motd: ['Velkommen til Hugin IRC-server!']
    });

    this.tlsForm = this.fb.group({
      certificateType: ['generate'],
      certificatePath: [''],
      certificatePassword: [''],
      requireTls: [true]
    });

    this.databaseForm = this.fb.group({
      host: ['localhost', [Validators.required]],
      port: [5432, [Validators.required]],
      username: ['postgres', [Validators.required]],
      password: ['', [Validators.required]],
      database: ['hugin', [Validators.required]]
    });

    this.adminForm = this.fb.group({
      username: ['admin', [Validators.required, Validators.minLength(3)]],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    }, { validators: this.passwordMatchValidator });
  }

  ngOnInit(): void {
    // Check if setup is already completed
    this.setupService.checkSetupRequired().subscribe({
      next: (result) => {
        if (!result.setupRequired) {
          this.router.navigate(['/login']);
        }
      },
      error: () => {
        // If we can't check, assume setup is required
      }
    });
  }

  passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    return password === confirmPassword ? null : { passwordMismatch: true };
  }

  passwordStrength = computed(() => {
    const password = this.adminForm.get('password')?.value || '';
    let strength = 0;
    if (password.length >= 8) strength++;
    if (/[A-Z]/.test(password)) strength++;
    if (/[a-z]/.test(password)) strength++;
    if (/[0-9]/.test(password)) strength++;
    if (/[^A-Za-z0-9]/.test(password)) strength++;
    return Math.min(strength, 4);
  });

  passwordStrengthText = computed(() => {
    const strength = this.passwordStrength();
    switch (strength) {
      case 0: return 'Veldig svakt';
      case 1: return 'Svakt';
      case 2: return 'Middels';
      case 3: return 'Sterkt';
      case 4: return 'Veldig sterkt';
      default: return '';
    }
  });

  canProceed(): boolean {
    switch (this.currentStep()) {
      case 'welcome':
        return true;
      case 'server':
        return this.serverForm.valid;
      case 'tls':
        if (this.tlsForm.get('certificateType')?.value === 'existing') {
          return !!this.tlsForm.get('certificatePath')?.value;
        }
        return true;
      case 'database':
        return this.databaseForm.valid && this.dbTestResult()?.success === true;
      case 'admin':
        return this.adminForm.valid;
      default:
        return false;
    }
  }

  testDatabase(): void {
    if (this.databaseForm.invalid) return;

    this.testingDb.set(true);
    this.dbTestResult.set(null);

    const config = this.databaseForm.value;
    this.setupService.testDatabase(config).subscribe({
      next: (result) => {
        this.testingDb.set(false);
        this.dbTestResult.set({
          success: result.success,
          message: result.success 
            ? 'Tilkobling vellykket!' 
            : (result.error || 'Kunne ikke koble til databasen')
        });
      },
      error: (err) => {
        this.testingDb.set(false);
        this.dbTestResult.set({
          success: false,
          message: err.error?.message || 'Kunne ikke koble til databasen'
        });
      }
    });
  }

  async nextStep(): Promise<void> {
    const stepIndex = this.currentStepIndex();
    if (stepIndex >= this.steps.length - 1) return;

    this.error.set(null);
    this.loading.set(true);

    try {
      // Save current step configuration
      switch (this.currentStep()) {
        case 'server':
          this.loadingMessage.set('Lagrer server-konfigurasjon...');
          await this.setupService.configureServer(this.serverForm.value).toPromise();
          break;
        case 'tls':
          this.loadingMessage.set('Konfigurerer TLS...');
          await this.setupService.configureTls(this.tlsForm.value).toPromise();
          break;
        case 'database':
          this.loadingMessage.set('Konfigurerer database...');
          // Database config is saved during test
          break;
        case 'admin':
          this.loadingMessage.set('Oppretter administratorkonto...');
          await this.setupService.createAdmin(this.adminForm.value).toPromise();
          this.loadingMessage.set('Fullfører oppsett...');
          await this.setupService.completeSetup().toPromise();
          break;
      }

      this.currentStep.set(this.steps[stepIndex + 1].id);
    } catch (err: any) {
      this.error.set(err.error?.message || 'En feil oppstod. Prøv igjen.');
    } finally {
      this.loading.set(false);
      this.loadingMessage.set('');
    }
  }

  previousStep(): void {
    const stepIndex = this.currentStepIndex();
    if (stepIndex > 0) {
      this.currentStep.set(this.steps[stepIndex - 1].id);
      this.error.set(null);
    }
  }

  goToLogin(): void {
    this.router.navigate(['/login']);
  }
}
