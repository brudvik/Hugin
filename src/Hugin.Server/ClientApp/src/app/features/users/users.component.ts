// Hugin Admin Panel - Users Management Component
import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '@core/services/api.service';
import { User, PagedResult } from '@core/models/api.models';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="users-page">
      <!-- Header -->
      <div class="page-header mb-4">
        <div>
          <h1>Brukere</h1>
          <p class="text-muted mb-0">Administrer tilkoblede og registrerte brukere</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
            <span class="d-none d-sm-inline ms-2">Oppdater</span>
          </button>
        </div>
      </div>

      <!-- Filters -->
      <div class="card mb-4">
        <div class="card-body">
          <div class="row g-3">
            <div class="col-md-4">
              <div class="input-group">
                <span class="input-group-text"><i class="fas fa-search"></i></span>
                <input type="text" class="form-control" placeholder="Søk etter bruker..."
                       [(ngModel)]="searchQuery" (keyup.enter)="search()">
              </div>
            </div>
            <div class="col-md-3">
              <select class="form-select" [(ngModel)]="filterStatus" (change)="refresh()">
                <option value="">Alle statuser</option>
                <option value="online">Tilkoblet</option>
                <option value="away">Borte</option>
                <option value="registered">Registrert</option>
              </select>
            </div>
            <div class="col-md-2">
              <button class="btn btn-primary w-100" (click)="search()">
                <i class="fas fa-search me-2"></i>Søk
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Users Table -->
      <div class="card">
        <div class="card-body p-0">
          @if (loading()) {
            <div class="text-center py-5">
              <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Laster...</span>
              </div>
            </div>
          } @else if (users().length === 0) {
            <div class="text-center py-5">
              <i class="fas fa-users fa-3x text-muted mb-3"></i>
              <p class="text-muted">Ingen brukere funnet</p>
            </div>
          } @else {
            <div class="table-responsive">
              <table class="table table-hover mb-0">
                <thead>
                  <tr>
                    <th>Bruker</th>
                    <th>Vert</th>
                    <th>Kanaler</th>
                    <th>Status</th>
                    <th>Tilkoblet</th>
                    <th class="text-end">Handlinger</th>
                  </tr>
                </thead>
                <tbody>
                  @for (user of users(); track user.nickname) {
                    <tr>
                      <td>
                        <div class="d-flex align-items-center">
                          <div class="user-avatar me-2">
                            <i class="fas fa-user"></i>
                          </div>
                          <div>
                            <div class="fw-medium">{{ user.nickname }}</div>
                            <small class="text-muted">{{ user.username }}&#64;{{ user.hostname }}</small>
                          </div>
                        </div>
                      </td>
                      <td>
                        <code class="text-muted">{{ user.hostname }}</code>
                      </td>
                      <td>
                        @if (user.channels && user.channels.length > 0) {
                          @for (channel of user.channels.slice(0, 3); track channel) {
                            <span class="badge bg-secondary me-1">#{{ channel }}</span>
                          }
                          @if (user.channels.length > 3) {
                            <span class="text-muted">+{{ user.channels.length - 3 }}</span>
                          }
                        } @else {
                          <span class="text-muted">-</span>
                        }
                      </td>
                      <td>
                        <span class="badge" [ngClass]="{
                          'bg-success': user.isOnline && !user.isAway,
                          'bg-warning': user.isAway,
                          'bg-secondary': !user.isOnline
                        }">
                          {{ user.isAway ? 'Borte' : (user.isOnline ? 'Online' : 'Offline') }}
                        </span>
                        @if (user.isOperator) {
                          <span class="badge bg-info ms-1">
                            <i class="fas fa-star"></i>
                          </span>
                        }
                        @if (user.isRegistered) {
                          <span class="badge bg-primary ms-1">
                            <i class="fas fa-check"></i>
                          </span>
                        }
                      </td>
                      <td>{{ formatTime(user.connectedAt) }}</td>
                      <td class="text-end">
                        <div class="btn-group btn-group-sm">
                          <button class="btn btn-outline-secondary" title="Vis detaljer"
                                  (click)="showUserDetails(user)">
                            <i class="fas fa-eye"></i>
                          </button>
                          <button class="btn btn-outline-warning" title="Send melding"
                                  (click)="sendMessage(user)">
                            <i class="fas fa-comment"></i>
                          </button>
                          <button class="btn btn-outline-danger" title="Koble fra"
                                  (click)="disconnectUser(user)">
                            <i class="fas fa-times"></i>
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Pagination -->
            @if (totalPages() > 1) {
              <div class="card-footer d-flex justify-content-between align-items-center">
                <small class="text-muted">
                  Viser {{ (currentPage() - 1) * pageSize + 1 }} - 
                  {{ Math.min(currentPage() * pageSize, totalItems()) }} av {{ totalItems() }}
                </small>
                <nav>
                  <ul class="pagination pagination-sm mb-0">
                    <li class="page-item" [class.disabled]="currentPage() === 1">
                      <button class="page-link" (click)="goToPage(currentPage() - 1)">
                        <i class="fas fa-chevron-left"></i>
                      </button>
                    </li>
                    @for (page of visiblePages(); track page) {
                      <li class="page-item" [class.active]="page === currentPage()">
                        <button class="page-link" (click)="goToPage(page)">{{ page }}</button>
                      </li>
                    }
                    <li class="page-item" [class.disabled]="currentPage() === totalPages()">
                      <button class="page-link" (click)="goToPage(currentPage() + 1)">
                        <i class="fas fa-chevron-right"></i>
                      </button>
                    </li>
                  </ul>
                </nav>
              </div>
            }
          }
        </div>
      </div>
    </div>

    <!-- User Details Modal -->
    @if (selectedUser()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeModal($event)">
        <div class="modal-dialog modal-dialog-centered">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                <i class="fas fa-user me-2"></i>
                {{ selectedUser()?.nickname }}
              </h5>
              <button type="button" class="btn-close" (click)="selectedUser.set(null)"></button>
            </div>
            <div class="modal-body">
              <dl class="row mb-0">
                <dt class="col-sm-4">Brukernavn</dt>
                <dd class="col-sm-8">{{ selectedUser()?.username }}</dd>
                
                <dt class="col-sm-4">Vert</dt>
                <dd class="col-sm-8"><code>{{ selectedUser()?.hostname }}</code></dd>
                
                <dt class="col-sm-4">Virkelig navn</dt>
                <dd class="col-sm-8">{{ selectedUser()?.realName || '-' }}</dd>
                
                <dt class="col-sm-4">Tilkoblet</dt>
                <dd class="col-sm-8">{{ selectedUser()?.connectedAt | date:'medium' }}</dd>
                
                <dt class="col-sm-4">Inaktiv siden</dt>
                <dd class="col-sm-8">{{ selectedUser()?.idleTime ? formatDuration(selectedUser()!.idleTime!) : '-' }}</dd>
                
                <dt class="col-sm-4">Kanaler</dt>
                <dd class="col-sm-8">
                  @if (selectedUser()?.channels?.length) {
                    @for (ch of selectedUser()?.channels; track ch) {
                      <span class="badge bg-secondary me-1">#{{ ch }}</span>
                    }
                  } @else {
                    <span class="text-muted">Ingen</span>
                  }
                </dd>
              </dl>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="selectedUser.set(null)">
                Lukk
              </button>
              <button type="button" class="btn btn-danger" (click)="disconnectUser(selectedUser()!)">
                <i class="fas fa-times me-2"></i>Koble fra
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

    .user-avatar {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: var(--bs-primary);
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

    .modal {
      background: rgba(0, 0, 0, 0.5);
    }

    .modal-content {
      background: var(--card-bg);
      border-color: var(--card-border);
    }

    .modal-header {
      border-color: var(--bs-border-color);
    }

    .modal-footer {
      border-color: var(--bs-border-color);
    }

    code {
      background: rgba(0, 0, 0, 0.3);
      padding: 0.125rem 0.375rem;
      border-radius: 0.25rem;
    }
  `]
})
export class UsersComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  loading = signal(false);
  users = signal<User[]>([]);
  selectedUser = signal<User | null>(null);
  
  searchQuery = '';
  filterStatus = '';
  
  currentPage = signal(1);
  pageSize = 20;
  totalItems = signal(0);
  totalPages = signal(0);
  
  Math = Math;

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    
    this.apiService.getUsers(this.currentPage(), this.pageSize, this.searchQuery || undefined)
      .subscribe({
        next: (result) => {
          this.users.set(result.items);
          this.totalItems.set(result.totalCount);
          this.totalPages.set(result.totalPages);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        }
      });
  }

  search(): void {
    this.currentPage.set(1);
    this.refresh();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
    this.refresh();
  }

  visiblePages(): number[] {
    const total = this.totalPages();
    const current = this.currentPage();
    const pages: number[] = [];
    
    let start = Math.max(1, current - 2);
    let end = Math.min(total, current + 2);
    
    if (end - start < 4) {
      if (start === 1) {
        end = Math.min(total, 5);
      } else {
        start = Math.max(1, total - 4);
      }
    }
    
    for (let i = start; i <= end; i++) {
      pages.push(i);
    }
    
    return pages;
  }

  formatTime(dateStr: string): string {
    if (!dateStr) return '-';
    const date = new Date(dateStr);
    const now = new Date();
    const diff = Math.floor((now.getTime() - date.getTime()) / 1000);
    
    if (diff < 60) return 'Nå';
    if (diff < 3600) return `${Math.floor(diff / 60)}m`;
    if (diff < 86400) return `${Math.floor(diff / 3600)}t`;
    return `${Math.floor(diff / 86400)}d`;
  }

  formatDuration(seconds: number): string {
    if (seconds < 60) return `${seconds}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
    return `${Math.floor(seconds / 3600)}t ${Math.floor((seconds % 3600) / 60)}m`;
  }

  showUserDetails(user: User): void {
    this.selectedUser.set(user);
  }

  sendMessage(user: User): void {
    const message = prompt(`Send melding til ${user.nickname}:`);
    if (message) {
      this.apiService.sendNotice(user.nickname, message).subscribe({
        next: () => alert('Melding sendt!'),
        error: () => alert('Kunne ikke sende melding')
      });
    }
  }

  disconnectUser(user: User): void {
    if (confirm(`Er du sikker på at du vil koble fra ${user.nickname}?`)) {
      const reason = prompt('Grunn (valgfritt):') || 'Frakoblet av administrator';
      this.apiService.disconnectUser(user.nickname, reason).subscribe({
        next: () => {
          this.selectedUser.set(null);
          this.refresh();
        },
        error: () => alert('Kunne ikke koble fra bruker')
      });
    }
  }

  closeModal(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal')) {
      this.selectedUser.set(null);
    }
  }
}
