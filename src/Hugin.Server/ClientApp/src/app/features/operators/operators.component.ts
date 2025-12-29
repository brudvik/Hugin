// Hugin Admin Panel - Operators Management Component
import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '@core/services/api.service';
import { Operator } from '@core/models/api.models';

@Component({
  selector: 'app-operators',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="operators-page">
      <!-- Header -->
      <div class="page-header mb-4">
        <div>
          <h1>Operatører</h1>
          <p class="text-muted mb-0">Administrer IRC-operatører (IRCops)</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary me-2" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
          </button>
          <button class="btn btn-primary" (click)="showAddModal()">
            <i class="fas fa-plus me-2"></i>Ny operatør
          </button>
        </div>
      </div>

      <!-- Operators Table -->
      <div class="card">
        <div class="card-body p-0">
          @if (loading()) {
            <div class="text-center py-5">
              <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Laster...</span>
              </div>
            </div>
          } @else if (operators().length === 0) {
            <div class="text-center py-5">
              <i class="fas fa-user-shield fa-3x text-muted mb-3"></i>
              <p class="text-muted mb-3">Ingen operatører konfigurert</p>
              <button class="btn btn-primary" (click)="showAddModal()">
                <i class="fas fa-plus me-2"></i>Legg til første operatør
              </button>
            </div>
          } @else {
            <div class="table-responsive">
              <table class="table table-hover mb-0">
                <thead>
                  <tr>
                    <th>Navn</th>
                    <th>Brukernavn</th>
                    <th>Hostmask</th>
                    <th>Rettigheter</th>
                    <th>Status</th>
                    <th class="text-end">Handlinger</th>
                  </tr>
                </thead>
                <tbody>
                  @for (op of operators(); track op.name) {
                    <tr>
                      <td>
                        <div class="d-flex align-items-center">
                          <div class="oper-avatar me-2">
                            <i class="fas fa-user-shield"></i>
                          </div>
                          <div>
                            <div class="fw-medium">{{ op.name }}</div>
                            @if (op.email) {
                              <small class="text-muted">{{ op.email }}</small>
                            }
                          </div>
                        </div>
                      </td>
                      <td><code>{{ op.username }}</code></td>
                      <td>
                        @if (op.hostmask) {
                          <code>{{ op.hostmask }}</code>
                        } @else {
                          <span class="text-muted">Alle</span>
                        }
                      </td>
                      <td>
                        @for (flag of op.flags || []; track flag) {
                          <span class="badge bg-primary me-1">{{ flag }}</span>
                        }
                        @if (!op.flags?.length) {
                          <span class="text-muted">Standard</span>
                        }
                      </td>
                      <td>
                        <span class="badge" [ngClass]="{
                          'bg-success': op.isOnline,
                          'bg-secondary': !op.isOnline
                        }">
                          {{ op.isOnline ? 'Online' : 'Offline' }}
                        </span>
                      </td>
                      <td class="text-end">
                        <div class="btn-group btn-group-sm">
                          <button class="btn btn-outline-primary" title="Rediger"
                                  (click)="editOperator(op)">
                            <i class="fas fa-edit"></i>
                          </button>
                          <button class="btn btn-outline-warning" title="Tilbakestill passord"
                                  (click)="resetPassword(op)">
                            <i class="fas fa-key"></i>
                          </button>
                          <button class="btn btn-outline-danger" title="Slett"
                                  (click)="deleteOperator(op)">
                            <i class="fas fa-trash"></i>
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      </div>

      <!-- Operator Flags Info -->
      <div class="card mt-4">
        <div class="card-header">
          <h5 class="mb-0">
            <i class="fas fa-info-circle me-2"></i>
            Operatør-rettigheter
          </h5>
        </div>
        <div class="card-body">
          <div class="row">
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">global</span></h6>
              <small class="text-muted">Full tilgang til alle serverkommandoer</small>
            </div>
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">local</span></h6>
              <small class="text-muted">Tilgang til lokale kommandoer</small>
            </div>
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">kill</span></h6>
              <small class="text-muted">Kan koble fra brukere med KILL</small>
            </div>
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">kline</span></h6>
              <small class="text-muted">Kan legge til K-lines (utestengelser)</small>
            </div>
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">gline</span></h6>
              <small class="text-muted">Kan legge til G-lines (globale utestengelser)</small>
            </div>
            <div class="col-md-4 mb-3">
              <h6><span class="badge bg-primary">rehash</span></h6>
              <small class="text-muted">Kan laste inn konfigurasjon på nytt</small>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Add/Edit Operator Modal -->
    @if (showModal()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeModal($event)">
        <div class="modal-dialog modal-dialog-centered">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                {{ editingOperator() ? 'Rediger operatør' : 'Ny operatør' }}
              </h5>
              <button type="button" class="btn-close" (click)="showModal.set(false)"></button>
            </div>
            <div class="modal-body">
              <div class="mb-3">
                <label class="form-label">Navn</label>
                <input type="text" class="form-control" [(ngModel)]="operForm.name"
                       placeholder="Operatørnavn">
              </div>
              <div class="mb-3">
                <label class="form-label">Brukernavn</label>
                <input type="text" class="form-control" [(ngModel)]="operForm.username"
                       placeholder="Brukernavn for OPER-kommando">
              </div>
              @if (!editingOperator()) {
                <div class="mb-3">
                  <label class="form-label">Passord</label>
                  <input type="password" class="form-control" [(ngModel)]="operForm.password"
                         placeholder="Passord for OPER-kommando">
                </div>
              }
              <div class="mb-3">
                <label class="form-label">Hostmask (valgfritt)</label>
                <input type="text" class="form-control" [(ngModel)]="operForm.hostmask"
                       placeholder="*@*.example.com">
                <small class="form-text text-muted">Begrens OPER-tilgang til spesifikke verter</small>
              </div>
              <div class="mb-3">
                <label class="form-label">E-post (valgfritt)</label>
                <input type="email" class="form-control" [(ngModel)]="operForm.email"
                       placeholder="oper@example.com">
              </div>
              <div class="mb-3">
                <label class="form-label">Rettigheter</label>
                <div class="row">
                  @for (flag of availableFlags; track flag) {
                    <div class="col-6">
                      <div class="form-check">
                        <input type="checkbox" class="form-check-input" [id]="'flag-' + flag"
                               [checked]="operForm.flags.includes(flag)"
                               (change)="toggleFlag(flag)">
                        <label class="form-check-label" [for]="'flag-' + flag">{{ flag }}</label>
                      </div>
                    </div>
                  }
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="showModal.set(false)">
                Avbryt
              </button>
              <button type="button" class="btn btn-primary" (click)="saveOperator()">
                <i class="fas fa-save me-2"></i>Lagre
              </button>
            </div>
          </div>
        </div>
      </div>
      <div class="modal-backdrop show"></div>
    }
  `,
  styles: [`
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: 1rem;
    }

    .page-header h1 {
      font-size: 1.75rem;
      font-weight: 700;
      margin-bottom: 0.25rem;
    }

    .oper-avatar {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: linear-gradient(135deg, #f59e0b, #d97706);
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
      font-size: 0.875rem;
    }

    .table th {
      font-weight: 600;
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--bs-gray-400);
      border-bottom-width: 1px;
    }

    .table td {
      vertical-align: middle;
    }

    code {
      background: rgba(0, 0, 0, 0.3);
      padding: 0.125rem 0.375rem;
      border-radius: 0.25rem;
    }

    .modal {
      background: rgba(0, 0, 0, 0.5);
    }

    .modal-content {
      background: var(--card-bg);
      border-color: var(--card-border);
    }

    .modal-header, .modal-footer {
      border-color: var(--bs-border-color);
    }
  `]
})
export class OperatorsComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  loading = signal(false);
  operators = signal<Operator[]>([]);
  showModal = signal(false);
  editingOperator = signal<Operator | null>(null);

  availableFlags = ['global', 'local', 'kill', 'kline', 'gline', 'rehash'];

  operForm = {
    name: '',
    username: '',
    password: '',
    hostmask: '',
    email: '',
    flags: [] as string[]
  };

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);

    this.apiService.getOperators().subscribe({
      next: (operators) => {
        this.operators.set(operators);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  showAddModal(): void {
    this.editingOperator.set(null);
    this.operForm = {
      name: '',
      username: '',
      password: '',
      hostmask: '',
      email: '',
      flags: ['local']
    };
    this.showModal.set(true);
  }

  editOperator(op: Operator): void {
    this.editingOperator.set(op);
    this.operForm = {
      name: op.name,
      username: op.username,
      password: '',
      hostmask: op.hostmask || '',
      email: op.email || '',
      flags: [...(op.flags || [])]
    };
    this.showModal.set(true);
  }

  toggleFlag(flag: string): void {
    const index = this.operForm.flags.indexOf(flag);
    if (index === -1) {
      this.operForm.flags.push(flag);
    } else {
      this.operForm.flags.splice(index, 1);
    }
  }

  saveOperator(): void {
    if (!this.operForm.name || !this.operForm.username) {
      alert('Navn og brukernavn er påkrevd');
      return;
    }

    if (!this.editingOperator() && !this.operForm.password) {
      alert('Passord er påkrevd for nye operatører');
      return;
    }

    const operData = {
      name: this.operForm.name,
      username: this.operForm.username,
      password: this.operForm.password || undefined,
      hostmask: this.operForm.hostmask || undefined,
      email: this.operForm.email || undefined,
      flags: this.operForm.flags
    };

    if (this.editingOperator()) {
      this.apiService.updateOperator(this.editingOperator()!.name, operData).subscribe({
        next: () => {
          this.showModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke oppdatere operatør: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    } else {
      this.apiService.createOperator(operData).subscribe({
        next: () => {
          this.showModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke opprette operatør: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  resetPassword(op: Operator): void {
    const newPassword = prompt(`Nytt passord for ${op.name}:`);
    if (newPassword) {
      this.apiService.updateOperator(op.name, { password: newPassword }).subscribe({
        next: () => {
          alert('Passord oppdatert!');
        },
        error: (err) => {
          alert('Kunne ikke oppdatere passord: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  deleteOperator(op: Operator): void {
    if (confirm(`Er du sikker på at du vil slette operatøren ${op.name}?`)) {
      this.apiService.deleteOperator(op.name).subscribe({
        next: () => {
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke slette operatør: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  closeModal(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal')) {
      this.showModal.set(false);
    }
  }
}
