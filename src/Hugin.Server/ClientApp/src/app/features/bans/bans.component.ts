// Hugin Admin Panel - Bans Management Component
import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '@core/services/api.service';
import { ServerBan } from '@core/models/api.models';

@Component({
  selector: 'app-bans',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="bans-page">
      <!-- Header -->
      <div class="page-header mb-4">
        <div>
          <h1>Utestengelser</h1>
          <p class="text-muted mb-0">Administrer server-bans (K-lines, G-lines)</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary me-2" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
          </button>
          <button class="btn btn-danger" (click)="showAddModal()">
            <i class="fas fa-ban me-2"></i>Ny utestengelse
          </button>
        </div>
      </div>

      <!-- Filter Tabs -->
      <ul class="nav nav-tabs mb-4">
        <li class="nav-item">
          <button class="nav-link" [class.active]="filterType() === 'all'"
                  (click)="filterType.set('all')">
            Alle
            <span class="badge bg-secondary ms-1">{{ totalCount() }}</span>
          </button>
        </li>
        <li class="nav-item">
          <button class="nav-link" [class.active]="filterType() === 'kline'"
                  (click)="filterType.set('kline')">
            K-Lines
            <span class="badge bg-warning ms-1">{{ klineCount() }}</span>
          </button>
        </li>
        <li class="nav-item">
          <button class="nav-link" [class.active]="filterType() === 'gline'"
                  (click)="filterType.set('gline')">
            G-Lines
            <span class="badge bg-danger ms-1">{{ glineCount() }}</span>
          </button>
        </li>
        <li class="nav-item">
          <button class="nav-link" [class.active]="filterType() === 'zline'"
                  (click)="filterType.set('zline')">
            Z-Lines
            <span class="badge bg-info ms-1">{{ zlineCount() }}</span>
          </button>
        </li>
      </ul>

      <!-- Bans Table -->
      <div class="card">
        <div class="card-body p-0">
          @if (loading()) {
            <div class="text-center py-5">
              <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Laster...</span>
              </div>
            </div>
          } @else if (filteredBans().length === 0) {
            <div class="text-center py-5">
              <i class="fas fa-ban fa-3x text-muted mb-3"></i>
              <p class="text-muted">Ingen utestengelser</p>
            </div>
          } @else {
            <div class="table-responsive">
              <table class="table table-hover mb-0">
                <thead>
                  <tr>
                    <th>Type</th>
                    <th>Maske</th>
                    <th>Grunn</th>
                    <th>Satt av</th>
                    <th>Utløper</th>
                    <th class="text-end">Handlinger</th>
                  </tr>
                </thead>
                <tbody>
                  @for (ban of filteredBans(); track ban.id) {
                    <tr [class.table-warning]="isExpiringSoon(ban)"
                        [class.table-danger]="ban.isPermanent">
                      <td>
                        <span class="badge" [ngClass]="{
                          'bg-warning': ban.type === 'kline',
                          'bg-danger': ban.type === 'gline',
                          'bg-info': ban.type === 'zline'
                        }">
                          {{ ban.type.toUpperCase() }}
                        </span>
                      </td>
                      <td><code>{{ ban.mask }}</code></td>
                      <td>
                        <span class="ban-reason" [title]="ban.reason">
                          {{ ban.reason }}
                        </span>
                      </td>
                      <td>
                        <small>{{ ban.setBy }}</small><br>
                        <small class="text-muted">{{ ban.setAt | date:'short' }}</small>
                      </td>
                      <td>
                        @if (ban.isPermanent) {
                          <span class="badge bg-dark">Permanent</span>
                        } @else if (ban.expiresAt) {
                          <span [class.text-warning]="isExpiringSoon(ban)">
                            {{ formatExpiry(ban.expiresAt) }}
                          </span>
                        } @else {
                          <span class="text-muted">-</span>
                        }
                      </td>
                      <td class="text-end">
                        <div class="btn-group btn-group-sm">
                          <button class="btn btn-outline-primary" title="Rediger"
                                  (click)="editBan(ban)">
                            <i class="fas fa-edit"></i>
                          </button>
                          <button class="btn btn-outline-success" title="Fjern"
                                  (click)="removeBan(ban)">
                            <i class="fas fa-times"></i>
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

      <!-- Ban Types Info -->
      <div class="row mt-4">
        <div class="col-md-4">
          <div class="card h-100">
            <div class="card-body">
              <h5><span class="badge bg-warning me-2">K-LINE</span>Lokal utestengelse</h5>
              <p class="text-muted small mb-0">
                Stenger ute brukere basert på ident@host fra denne serveren.
                Påvirker kun tilkoblinger til denne serveren.
              </p>
            </div>
          </div>
        </div>
        <div class="col-md-4">
          <div class="card h-100">
            <div class="card-body">
              <h5><span class="badge bg-danger me-2">G-LINE</span>Global utestengelse</h5>
              <p class="text-muted small mb-0">
                Stenger ute brukere fra hele nettverket.
                Synkroniseres mellom alle servere i nettverket.
              </p>
            </div>
          </div>
        </div>
        <div class="col-md-4">
          <div class="card h-100">
            <div class="card-body">
              <h5><span class="badge bg-info me-2">Z-LINE</span>IP-utestengelse</h5>
              <p class="text-muted small mb-0">
                Stenger ute basert på IP-adresse.
                Brukes for alvorlige tilfeller eller botnett.
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Add/Edit Ban Modal -->
    @if (showModal()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeModal($event)">
        <div class="modal-dialog modal-dialog-centered">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                {{ editingBan() ? 'Rediger utestengelse' : 'Ny utestengelse' }}
              </h5>
              <button type="button" class="btn-close" (click)="showModal.set(false)"></button>
            </div>
            <div class="modal-body">
              <div class="mb-3">
                <label class="form-label">Type</label>
                <select class="form-select" [(ngModel)]="banForm.type" [disabled]="!!editingBan()">
                  <option value="kline">K-Line (lokal)</option>
                  <option value="gline">G-Line (global)</option>
                  <option value="zline">Z-Line (IP)</option>
                </select>
              </div>
              <div class="mb-3">
                <label class="form-label">Maske</label>
                <input type="text" class="form-control" [(ngModel)]="banForm.mask"
                       [placeholder]="getMaskPlaceholder()"
                       [disabled]="!!editingBan()">
                <small class="form-text text-muted">
                  {{ getMaskHelp() }}
                </small>
              </div>
              <div class="mb-3">
                <label class="form-label">Grunn</label>
                <textarea class="form-control" [(ngModel)]="banForm.reason" rows="2"
                          placeholder="Grunn for utestengelse..."></textarea>
              </div>
              <div class="mb-3">
                <label class="form-label">Varighet</label>
                <div class="row">
                  <div class="col-8">
                    <select class="form-select" [(ngModel)]="banForm.duration">
                      <option value="1h">1 time</option>
                      <option value="6h">6 timer</option>
                      <option value="1d">1 dag</option>
                      <option value="7d">1 uke</option>
                      <option value="30d">30 dager</option>
                      <option value="365d">1 år</option>
                      <option value="permanent">Permanent</option>
                      <option value="custom">Egendefinert...</option>
                    </select>
                  </div>
                  @if (banForm.duration === 'custom') {
                    <div class="col-4">
                      <input type="number" class="form-control" [(ngModel)]="banForm.customDays"
                             placeholder="Dager" min="1">
                    </div>
                  }
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="showModal.set(false)">
                Avbryt
              </button>
              <button type="button" class="btn btn-danger" (click)="saveBan()">
                <i class="fas fa-ban me-2"></i>
                {{ editingBan() ? 'Oppdater' : 'Legg til utestengelse' }}
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

    .nav-tabs {
      border-color: var(--bs-border-color);
    }

    .nav-tabs .nav-link {
      color: var(--bs-gray-400);
      border-color: transparent;
    }

    .nav-tabs .nav-link:hover {
      color: var(--bs-light);
      border-color: transparent;
    }

    .nav-tabs .nav-link.active {
      color: var(--bs-light);
      background: var(--card-bg);
      border-color: var(--bs-border-color) var(--bs-border-color) var(--card-bg);
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

    .ban-reason {
      display: inline-block;
      max-width: 200px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
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
export class BansComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  loading = signal(false);
  bans = signal<ServerBan[]>([]);
  filterType = signal<'all' | 'kline' | 'gline' | 'zline'>('all');
  showModal = signal(false);
  editingBan = signal<ServerBan | null>(null);

  banForm = {
    type: 'kline' as 'kline' | 'gline' | 'zline',
    mask: '',
    reason: '',
    duration: '1d',
    customDays: 7
  };

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);

    this.apiService.getBans().subscribe({
      next: (bans) => {
        this.bans.set(bans);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  filteredBans(): ServerBan[] {
    const type = this.filterType();
    if (type === 'all') {
      return this.bans();
    }
    return this.bans().filter(b => b.type === type);
  }

  totalCount(): number {
    return this.bans().length;
  }

  klineCount(): number {
    return this.bans().filter(b => b.type === 'kline').length;
  }

  glineCount(): number {
    return this.bans().filter(b => b.type === 'gline').length;
  }

  zlineCount(): number {
    return this.bans().filter(b => b.type === 'zline').length;
  }

  isExpiringSoon(ban: ServerBan): boolean {
    if (ban.isPermanent || !ban.expiresAt) return false;
    const expiresAt = new Date(ban.expiresAt);
    const hoursUntilExpiry = (expiresAt.getTime() - Date.now()) / (1000 * 60 * 60);
    return hoursUntilExpiry > 0 && hoursUntilExpiry < 24;
  }

  formatExpiry(expiresAt: string): string {
    const date = new Date(expiresAt);
    const now = new Date();
    const diff = date.getTime() - now.getTime();

    if (diff <= 0) return 'Utløpt';

    const hours = Math.floor(diff / (1000 * 60 * 60));
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d ${hours % 24}t`;
    if (hours > 0) return `${hours}t`;
    return `${Math.floor(diff / (1000 * 60))}m`;
  }

  getMaskPlaceholder(): string {
    switch (this.banForm.type) {
      case 'kline': return '*@*.example.com';
      case 'gline': return '*@*.example.com';
      case 'zline': return '192.168.1.*';
      default: return '*@*';
    }
  }

  getMaskHelp(): string {
    switch (this.banForm.type) {
      case 'kline':
      case 'gline':
        return 'Format: ident@host (bruk * som wildcard)';
      case 'zline':
        return 'Format: IP-adresse eller CIDR (f.eks. 192.168.1.0/24)';
      default:
        return '';
    }
  }

  showAddModal(): void {
    this.editingBan.set(null);
    this.banForm = {
      type: 'kline',
      mask: '',
      reason: '',
      duration: '1d',
      customDays: 7
    };
    this.showModal.set(true);
  }

  editBan(ban: ServerBan): void {
    this.editingBan.set(ban);
    this.banForm = {
      type: ban.type,
      mask: ban.mask,
      reason: ban.reason,
      duration: ban.isPermanent ? 'permanent' : '1d',
      customDays: 7
    };
    this.showModal.set(true);
  }

  saveBan(): void {
    if (!this.banForm.mask || !this.banForm.reason) {
      alert('Maske og grunn er påkrevd');
      return;
    }

    let durationSeconds: number | null = null;
    switch (this.banForm.duration) {
      case '1h': durationSeconds = 3600; break;
      case '6h': durationSeconds = 6 * 3600; break;
      case '1d': durationSeconds = 86400; break;
      case '7d': durationSeconds = 7 * 86400; break;
      case '30d': durationSeconds = 30 * 86400; break;
      case '365d': durationSeconds = 365 * 86400; break;
      case 'permanent': durationSeconds = null; break;
      case 'custom': durationSeconds = this.banForm.customDays * 86400; break;
    }

    const banData = {
      type: this.banForm.type,
      mask: this.banForm.mask,
      reason: this.banForm.reason,
      duration: durationSeconds,
      isPermanent: this.banForm.duration === 'permanent'
    };

    if (this.editingBan()) {
      this.apiService.updateBan(this.editingBan()!.id!, banData).subscribe({
        next: () => {
          this.showModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke oppdatere utestengelse: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    } else {
      this.apiService.createBan(banData).subscribe({
        next: () => {
          this.showModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke opprette utestengelse: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  removeBan(ban: ServerBan): void {
    if (confirm(`Er du sikker på at du vil fjerne utestengelsen for ${ban.mask}?`)) {
      this.apiService.deleteBan(ban.id!).subscribe({
        next: () => {
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke fjerne utestengelse: ' + (err.error?.message || 'Ukjent feil'));
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
