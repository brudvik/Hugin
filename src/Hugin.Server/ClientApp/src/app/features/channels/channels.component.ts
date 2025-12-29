// Hugin Admin Panel - Channels Management Component
import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '@core/services/api.service';
import { Channel } from '@core/models/api.models';

@Component({
  selector: 'app-channels',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="channels-page">
      <!-- Header -->
      <div class="page-header mb-4">
        <div>
          <h1>Kanaler</h1>
          <p class="text-muted mb-0">Administrer IRC-kanaler</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary me-2" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
          </button>
          <button class="btn btn-primary" (click)="showCreateModal()">
            <i class="fas fa-plus me-2"></i>Ny kanal
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
                <input type="text" class="form-control" placeholder="Søk etter kanal..."
                       [(ngModel)]="searchQuery" (keyup.enter)="search()">
              </div>
            </div>
            <div class="col-md-3">
              <select class="form-select" [(ngModel)]="filterType" (change)="refresh()">
                <option value="">Alle typer</option>
                <option value="public">Offentlige</option>
                <option value="private">Private</option>
                <option value="secret">Hemmelige</option>
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

      <!-- Channels Grid -->
      @if (loading()) {
        <div class="text-center py-5">
          <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Laster...</span>
          </div>
        </div>
      } @else if (channels().length === 0) {
        <div class="card">
          <div class="card-body text-center py-5">
            <i class="fas fa-hashtag fa-3x text-muted mb-3"></i>
            <p class="text-muted">Ingen kanaler funnet</p>
          </div>
        </div>
      } @else {
        <div class="row g-4">
          @for (channel of channels(); track channel.name) {
            <div class="col-md-6 col-lg-4">
              <div class="card channel-card h-100">
                <div class="card-header d-flex justify-content-between align-items-center">
                  <h5 class="mb-0">
                    <i class="fas fa-hashtag text-muted me-1"></i>
                    {{ channel.name }}
                  </h5>
                  <div class="channel-badges">
                    @if (channel.isPrivate) {
                      <span class="badge bg-warning" title="Privat">
                        <i class="fas fa-lock"></i>
                      </span>
                    }
                    @if (channel.isSecret) {
                      <span class="badge bg-danger" title="Hemmelig">
                        <i class="fas fa-eye-slash"></i>
                      </span>
                    }
                    @if (channel.isModerated) {
                      <span class="badge bg-info" title="Moderert">
                        <i class="fas fa-microphone-slash"></i>
                      </span>
                    }
                  </div>
                </div>
                <div class="card-body">
                  <p class="channel-topic mb-3">
                    {{ channel.topic || 'Ingen tema satt' }}
                  </p>
                  <div class="channel-stats d-flex gap-4">
                    <div class="stat">
                      <i class="fas fa-users text-primary me-1"></i>
                      <span>{{ channel.userCount }} brukere</span>
                    </div>
                    <div class="stat">
                      <i class="fas fa-comment text-success me-1"></i>
                      <span>{{ channel.messageCount || 0 }} meldinger</span>
                    </div>
                  </div>
                </div>
                <div class="card-footer">
                  <div class="btn-group btn-group-sm w-100">
                    <button class="btn btn-outline-secondary" (click)="showChannelDetails(channel)">
                      <i class="fas fa-eye me-1"></i>Detaljer
                    </button>
                    <button class="btn btn-outline-primary" (click)="editChannel(channel)">
                      <i class="fas fa-edit me-1"></i>Rediger
                    </button>
                    <button class="btn btn-outline-danger" (click)="deleteChannel(channel)">
                      <i class="fas fa-trash me-1"></i>Slett
                    </button>
                  </div>
                </div>
              </div>
            </div>
          }
        </div>

        <!-- Pagination -->
        @if (totalPages() > 1) {
          <div class="d-flex justify-content-center mt-4">
            <nav>
              <ul class="pagination">
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

    <!-- Channel Details Modal -->
    @if (selectedChannel()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeModal($event)">
        <div class="modal-dialog modal-dialog-centered modal-lg">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                <i class="fas fa-hashtag me-2"></i>
                {{ selectedChannel()?.name }}
              </h5>
              <button type="button" class="btn-close" (click)="selectedChannel.set(null)"></button>
            </div>
            <div class="modal-body">
              <div class="row">
                <div class="col-md-6">
                  <h6 class="text-muted mb-3">Kanalinformasjon</h6>
                  <dl class="row mb-0">
                    <dt class="col-sm-4">Tema</dt>
                    <dd class="col-sm-8">{{ selectedChannel()?.topic || '-' }}</dd>
                    
                    <dt class="col-sm-4">Opprettet</dt>
                    <dd class="col-sm-8">{{ selectedChannel()?.createdAt | date:'medium' }}</dd>
                    
                    <dt class="col-sm-4">Brukere</dt>
                    <dd class="col-sm-8">{{ selectedChannel()?.userCount }}</dd>
                    
                    <dt class="col-sm-4">Moduser</dt>
                    <dd class="col-sm-8">
                      <code>{{ selectedChannel()?.modes || '+' }}</code>
                    </dd>
                  </dl>
                </div>
                <div class="col-md-6">
                  <h6 class="text-muted mb-3">Brukerliste</h6>
                  <div class="user-list">
                    @if (selectedChannel()?.users?.length) {
                      @for (user of selectedChannel()?.users; track user) {
                        <span class="badge bg-secondary me-1 mb-1">{{ user }}</span>
                      }
                    } @else {
                      <span class="text-muted">Ingen brukere</span>
                    }
                  </div>
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="selectedChannel.set(null)">
                Lukk
              </button>
            </div>
          </div>
        </div>
      </div>
      <div class="modal-backdrop show"></div>
    }

    <!-- Create/Edit Channel Modal -->
    @if (showEditModal()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeEditModal($event)">
        <div class="modal-dialog modal-dialog-centered">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                {{ editingChannel() ? 'Rediger kanal' : 'Opprett kanal' }}
              </h5>
              <button type="button" class="btn-close" (click)="showEditModal.set(false)"></button>
            </div>
            <div class="modal-body">
              <div class="mb-3">
                <label class="form-label">Kanalnavn</label>
                <div class="input-group">
                  <span class="input-group-text">#</span>
                  <input type="text" class="form-control" [(ngModel)]="editForm.name"
                         [disabled]="!!editingChannel()" placeholder="kanalnavn">
                </div>
              </div>
              <div class="mb-3">
                <label class="form-label">Tema</label>
                <input type="text" class="form-control" [(ngModel)]="editForm.topic"
                       placeholder="Kanalens tema...">
              </div>
              <div class="row mb-3">
                <div class="col-6">
                  <div class="form-check">
                    <input type="checkbox" class="form-check-input" id="isPrivate"
                           [(ngModel)]="editForm.isPrivate">
                    <label class="form-check-label" for="isPrivate">Privat (+p)</label>
                  </div>
                </div>
                <div class="col-6">
                  <div class="form-check">
                    <input type="checkbox" class="form-check-input" id="isSecret"
                           [(ngModel)]="editForm.isSecret">
                    <label class="form-check-label" for="isSecret">Hemmelig (+s)</label>
                  </div>
                </div>
                <div class="col-6">
                  <div class="form-check">
                    <input type="checkbox" class="form-check-input" id="isModerated"
                           [(ngModel)]="editForm.isModerated">
                    <label class="form-check-label" for="isModerated">Moderert (+m)</label>
                  </div>
                </div>
                <div class="col-6">
                  <div class="form-check">
                    <input type="checkbox" class="form-check-input" id="isInviteOnly"
                           [(ngModel)]="editForm.isInviteOnly">
                    <label class="form-check-label" for="isInviteOnly">Kun invitasjon (+i)</label>
                  </div>
                </div>
              </div>
              <div class="mb-3">
                <label class="form-label">Brukergrense (0 = ingen grense)</label>
                <input type="number" class="form-control" [(ngModel)]="editForm.userLimit"
                       min="0" max="9999">
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="showEditModal.set(false)">
                Avbryt
              </button>
              <button type="button" class="btn btn-primary" (click)="saveChannel()">
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

    .channel-card {
      transition: transform 0.2s ease, box-shadow 0.2s ease;
    }

    .channel-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    }

    .channel-card .card-header {
      background: transparent;
      border-bottom-color: var(--bs-border-color);
    }

    .channel-card .card-footer {
      background: transparent;
      border-top-color: var(--bs-border-color);
    }

    .channel-topic {
      color: var(--bs-gray-400);
      font-size: 0.9rem;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .channel-stats .stat {
      font-size: 0.85rem;
      color: var(--bs-gray-400);
    }

    .channel-badges .badge {
      font-size: 0.7rem;
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

    .user-list {
      max-height: 200px;
      overflow-y: auto;
    }
  `]
})
export class ChannelsComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  loading = signal(false);
  channels = signal<Channel[]>([]);
  selectedChannel = signal<Channel | null>(null);
  showEditModal = signal(false);
  editingChannel = signal<Channel | null>(null);

  searchQuery = '';
  filterType = '';

  currentPage = signal(1);
  pageSize = 12;
  totalItems = signal(0);
  totalPages = signal(0);

  editForm = {
    name: '',
    topic: '',
    isPrivate: false,
    isSecret: false,
    isModerated: false,
    isInviteOnly: false,
    userLimit: 0
  };

  Math = Math;

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);

    this.apiService.getChannels(this.currentPage(), this.pageSize, this.searchQuery || undefined)
      .subscribe({
        next: (result) => {
          this.channels.set(result.items);
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

  showChannelDetails(channel: Channel): void {
    this.selectedChannel.set(channel);
  }

  showCreateModal(): void {
    this.editingChannel.set(null);
    this.editForm = {
      name: '',
      topic: '',
      isPrivate: false,
      isSecret: false,
      isModerated: false,
      isInviteOnly: false,
      userLimit: 0
    };
    this.showEditModal.set(true);
  }

  editChannel(channel: Channel): void {
    this.editingChannel.set(channel);
    this.editForm = {
      name: channel.name,
      topic: channel.topic || '',
      isPrivate: channel.isPrivate || false,
      isSecret: channel.isSecret || false,
      isModerated: channel.isModerated || false,
      isInviteOnly: channel.isInviteOnly || false,
      userLimit: channel.userLimit || 0
    };
    this.showEditModal.set(true);
  }

  saveChannel(): void {
    if (!this.editForm.name) {
      alert('Kanalnavn er påkrevd');
      return;
    }

    const channelData = {
      name: this.editForm.name.startsWith('#') ? this.editForm.name : '#' + this.editForm.name,
      topic: this.editForm.topic,
      isPrivate: this.editForm.isPrivate,
      isSecret: this.editForm.isSecret,
      isModerated: this.editForm.isModerated,
      isInviteOnly: this.editForm.isInviteOnly,
      userLimit: this.editForm.userLimit
    };

    if (this.editingChannel()) {
      this.apiService.updateChannel(channelData.name, channelData).subscribe({
        next: () => {
          this.showEditModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke oppdatere kanal: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    } else {
      this.apiService.createChannel(channelData).subscribe({
        next: () => {
          this.showEditModal.set(false);
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke opprette kanal: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  deleteChannel(channel: Channel): void {
    if (confirm(`Er du sikker på at du vil slette kanalen ${channel.name}?`)) {
      this.apiService.deleteChannel(channel.name).subscribe({
        next: () => {
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke slette kanal: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  closeModal(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal')) {
      this.selectedChannel.set(null);
    }
  }

  closeEditModal(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal')) {
      this.showEditModal.set(false);
    }
  }
}
