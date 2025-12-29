// Hugin Admin Panel - Configuration Component
import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '@core/services/api.service';
import { ServerConfig } from '@core/models/api.models';

@Component({
  selector: 'app-config',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="config-page">
      <!-- Header -->
      <div class="page-header mb-4">
        <div>
          <h1>Konfigurasjon</h1>
          <p class="text-muted mb-0">Administrer serverinnstillinger</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary me-2" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
          </button>
          <button class="btn btn-primary" (click)="saveConfig()" [disabled]="saving() || !hasChanges()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-2"></span>
              Lagrer...
            } @else {
              <i class="fas fa-save me-2"></i>
              Lagre endringer
            }
          </button>
        </div>
      </div>

      @if (loading()) {
        <div class="text-center py-5">
          <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Laster...</span>
          </div>
        </div>
      } @else {
        <!-- Config Sections -->
        <div class="row g-4">
          <!-- Server Settings -->
          <div class="col-lg-6">
            <div class="card">
              <div class="card-header">
                <h5 class="mb-0">
                  <i class="fas fa-server me-2"></i>
                  Server
                </h5>
              </div>
              <div class="card-body">
                <div class="mb-3">
                  <label class="form-label">Servernavn</label>
                  <input type="text" class="form-control" [(ngModel)]="config.serverName"
                         placeholder="irc.example.com">
                  <small class="form-text text-muted">Serverens FQDN</small>
                </div>
                <div class="mb-3">
                  <label class="form-label">Nettverksnavn</label>
                  <input type="text" class="form-control" [(ngModel)]="config.networkName"
                         placeholder="MyNetwork">
                </div>
                <div class="row">
                  <div class="col-6 mb-3">
                    <label class="form-label">TLS-port</label>
                    <input type="number" class="form-control" [(ngModel)]="config.tlsPort"
                           min="1" max="65535">
                  </div>
                  <div class="col-6 mb-3">
                    <label class="form-label">Admin-port</label>
                    <input type="number" class="form-control" [(ngModel)]="config.adminPort"
                           min="1" max="65535">
                  </div>
                </div>
                <div class="mb-3">
                  <label class="form-label">Maks tilkoblinger</label>
                  <input type="number" class="form-control" [(ngModel)]="config.maxConnections"
                         min="1">
                </div>
              </div>
            </div>
          </div>

          <!-- Connection Settings -->
          <div class="col-lg-6">
            <div class="card">
              <div class="card-header">
                <h5 class="mb-0">
                  <i class="fas fa-plug me-2"></i>
                  Tilkoblinger
                </h5>
              </div>
              <div class="card-body">
                <div class="mb-3">
                  <label class="form-label">Ping-intervall (sekunder)</label>
                  <input type="number" class="form-control" [(ngModel)]="config.pingInterval"
                         min="30" max="600">
                </div>
                <div class="mb-3">
                  <label class="form-label">Ping-timeout (sekunder)</label>
                  <input type="number" class="form-control" [(ngModel)]="config.pingTimeout"
                         min="30" max="300">
                </div>
                <div class="mb-3">
                  <label class="form-label">Registreringstimeout (sekunder)</label>
                  <input type="number" class="form-control" [(ngModel)]="config.registrationTimeout"
                         min="10" max="300">
                </div>
                <div class="form-check mb-3">
                  <input type="checkbox" class="form-check-input" id="requireTls"
                         [(ngModel)]="config.requireTls">
                  <label class="form-check-label" for="requireTls">
                    Krev TLS for alle tilkoblinger
                  </label>
                </div>
                <div class="form-check">
                  <input type="checkbox" class="form-check-input" id="cloakHostnames"
                         [(ngModel)]="config.cloakHostnames">
                  <label class="form-check-label" for="cloakHostnames">
                    Skjul bruker-hostnavn (cloaking)
                  </label>
                </div>
              </div>
            </div>
          </div>

          <!-- Rate Limiting -->
          <div class="col-lg-6">
            <div class="card">
              <div class="card-header">
                <h5 class="mb-0">
                  <i class="fas fa-tachometer-alt me-2"></i>
                  Hastighetsbegrensning
                </h5>
              </div>
              <div class="card-body">
                <div class="mb-3">
                  <label class="form-label">Meldinger per sekund</label>
                  <input type="number" class="form-control" [(ngModel)]="config.messagesPerSecond"
                         min="1" max="100" step="0.5">
                </div>
                <div class="mb-3">
                  <label class="form-label">Tilkoblingsforsøk per minutt</label>
                  <input type="number" class="form-control" [(ngModel)]="config.connectionsPerMinute"
                         min="1" max="60">
                </div>
                <div class="mb-3">
                  <label class="form-label">Kommandoer per sekund</label>
                  <input type="number" class="form-control" [(ngModel)]="config.commandsPerSecond"
                         min="1" max="50">
                </div>
                <div class="form-check">
                  <input type="checkbox" class="form-check-input" id="enableFloodProtection"
                         [(ngModel)]="config.enableFloodProtection">
                  <label class="form-check-label" for="enableFloodProtection">
                    Aktiver flombeskyttelse
                  </label>
                </div>
              </div>
            </div>
          </div>

          <!-- Channel Settings -->
          <div class="col-lg-6">
            <div class="card">
              <div class="card-header">
                <h5 class="mb-0">
                  <i class="fas fa-hashtag me-2"></i>
                  Kanaler
                </h5>
              </div>
              <div class="card-body">
                <div class="mb-3">
                  <label class="form-label">Maks kanaler per bruker</label>
                  <input type="number" class="form-control" [(ngModel)]="config.maxChannelsPerUser"
                         min="1" max="100">
                </div>
                <div class="mb-3">
                  <label class="form-label">Maks brukere per kanal</label>
                  <input type="number" class="form-control" [(ngModel)]="config.maxUsersPerChannel"
                         min="1" max="10000">
                </div>
                <div class="mb-3">
                  <label class="form-label">Maks tema-lengde</label>
                  <input type="number" class="form-control" [(ngModel)]="config.maxTopicLength"
                         min="100" max="1000">
                </div>
                <div class="form-check">
                  <input type="checkbox" class="form-check-input" id="allowChannelCreation"
                         [(ngModel)]="config.allowChannelCreation">
                  <label class="form-check-label" for="allowChannelCreation">
                    Tillat brukere å opprette kanaler
                  </label>
                </div>
              </div>
            </div>
          </div>

          <!-- MOTD -->
          <div class="col-12">
            <div class="card">
              <div class="card-header d-flex justify-content-between align-items-center">
                <h5 class="mb-0">
                  <i class="fas fa-comment-alt me-2"></i>
                  Message of the Day (MOTD)
                </h5>
                <button class="btn btn-sm btn-outline-secondary" (click)="previewMotd()">
                  <i class="fas fa-eye me-1"></i>
                  Forhåndsvis
                </button>
              </div>
              <div class="card-body">
                <textarea class="form-control font-monospace" [(ngModel)]="config.motd" rows="10"
                          placeholder="Velkommen til Hugin IRC Server!&#10;&#10;- Følg kanalreglene&#10;- Vær hyggelig mot andre brukere"></textarea>
                <small class="form-text text-muted">
                  Denne meldingen vises til brukere når de kobler til. Bruk vanlige linjeskift.
                </small>
              </div>
            </div>
          </div>

          <!-- IRCv3 Capabilities -->
          <div class="col-12">
            <div class="card">
              <div class="card-header">
                <h5 class="mb-0">
                  <i class="fas fa-cogs me-2"></i>
                  IRCv3 Capabilities
                </h5>
              </div>
              <div class="card-body">
                <div class="row">
                  @for (cap of availableCapabilities; track cap.id) {
                    <div class="col-md-4 col-lg-3 mb-3">
                      <div class="form-check">
                        <input type="checkbox" class="form-check-input" [id]="'cap-' + cap.id"
                               [checked]="config.enabledCapabilities?.includes(cap.id)"
                               (change)="toggleCapability(cap.id)">
                        <label class="form-check-label" [for]="'cap-' + cap.id">
                          {{ cap.name }}
                        </label>
                        <small class="d-block text-muted">{{ cap.description }}</small>
                      </div>
                    </div>
                  }
                </div>
              </div>
            </div>
          </div>
        </div>
      }
    </div>

    <!-- MOTD Preview Modal -->
    @if (showMotdPreview()) {
      <div class="modal show d-block" tabindex="-1" (click)="closeMotdPreview($event)">
        <div class="modal-dialog modal-dialog-centered modal-lg">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">
                <i class="fas fa-eye me-2"></i>
                MOTD Forhåndsvisning
              </h5>
              <button type="button" class="btn-close" (click)="showMotdPreview.set(false)"></button>
            </div>
            <div class="modal-body">
              <div class="motd-preview">
                <div class="motd-line">:{{ config.serverName }} 375 bruker :- {{ config.serverName }} Message of the Day -</div>
                @for (line of motdLines(); track $index) {
                  <div class="motd-line">:{{ config.serverName }} 372 bruker :- {{ line }}</div>
                }
                <div class="motd-line">:{{ config.serverName }} 376 bruker :End of /MOTD command.</div>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" (click)="showMotdPreview.set(false)">
                Lukk
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

    .card-header {
      background: transparent;
      border-bottom-color: var(--bs-border-color);
    }

    .card-header h5 {
      font-size: 1rem;
    }

    .font-monospace {
      font-family: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
      font-size: 0.875rem;
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

    .motd-preview {
      background: #0d1117;
      border-radius: 0.5rem;
      padding: 1rem;
      font-family: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
      font-size: 0.8rem;
      max-height: 400px;
      overflow-y: auto;
    }

    .motd-line {
      color: #7ee787;
      white-space: pre-wrap;
      word-break: break-all;
    }
  `]
})
export class ConfigComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  loading = signal(false);
  saving = signal(false);
  showMotdPreview = signal(false);

  config: ServerConfig = this.getDefaultConfig();
  originalConfig: ServerConfig = this.getDefaultConfig();

  availableCapabilities = [
    { id: 'message-tags', name: 'message-tags', description: 'Støtte for meldingstagger' },
    { id: 'server-time', name: 'server-time', description: 'Servertidsstempel' },
    { id: 'echo-message', name: 'echo-message', description: 'Ekko av egne meldinger' },
    { id: 'batch', name: 'batch', description: 'Batching av meldinger' },
    { id: 'labeled-response', name: 'labeled-response', description: 'Merkede svar' },
    { id: 'multi-prefix', name: 'multi-prefix', description: 'Flere prefikser i NAMES' },
    { id: 'extended-join', name: 'extended-join', description: 'Utvidet JOIN-info' },
    { id: 'account-notify', name: 'account-notify', description: 'Kontovarsler' },
    { id: 'away-notify', name: 'away-notify', description: 'Borte-varsler' },
    { id: 'chghost', name: 'chghost', description: 'Vertsendringsvarsler' },
    { id: 'invite-notify', name: 'invite-notify', description: 'Invitasjonsvarsler' },
    { id: 'userhost-in-names', name: 'userhost-in-names', description: 'Brukervert i NAMES' },
    { id: 'cap-notify', name: 'cap-notify', description: 'CAP-endringsvarsler' },
    { id: 'sasl', name: 'sasl', description: 'SASL-autentisering' },
    { id: 'account-tag', name: 'account-tag', description: 'Kontotag i meldinger' },
    { id: 'typing', name: 'typing', description: 'Skriver-indikator' }
  ];

  ngOnInit(): void {
    this.refresh();
  }

  getDefaultConfig(): ServerConfig {
    return {
      serverName: '',
      networkName: '',
      tlsPort: 6697,
      adminPort: 9443,
      maxConnections: 1000,
      pingInterval: 120,
      pingTimeout: 60,
      registrationTimeout: 60,
      requireTls: true,
      cloakHostnames: true,
      messagesPerSecond: 5,
      connectionsPerMinute: 10,
      commandsPerSecond: 10,
      enableFloodProtection: true,
      maxChannelsPerUser: 20,
      maxUsersPerChannel: 1000,
      maxTopicLength: 390,
      allowChannelCreation: true,
      motd: '',
      enabledCapabilities: []
    };
  }

  refresh(): void {
    this.loading.set(true);

    this.apiService.getConfig().subscribe({
      next: (config) => {
        this.config = { ...config };
        this.originalConfig = { ...config };
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  hasChanges(): boolean {
    return JSON.stringify(this.config) !== JSON.stringify(this.originalConfig);
  }

  toggleCapability(capId: string): void {
    if (!this.config.enabledCapabilities) {
      this.config.enabledCapabilities = [];
    }

    const index = this.config.enabledCapabilities.indexOf(capId);
    if (index === -1) {
      this.config.enabledCapabilities.push(capId);
    } else {
      this.config.enabledCapabilities.splice(index, 1);
    }
  }

  saveConfig(): void {
    this.saving.set(true);

    this.apiService.updateConfig(this.config).subscribe({
      next: () => {
        this.originalConfig = { ...this.config };
        this.saving.set(false);
        alert('Konfigurasjon lagret!');
      },
      error: (err) => {
        this.saving.set(false);
        alert('Kunne ikke lagre konfigurasjon: ' + (err.error?.message || 'Ukjent feil'));
      }
    });
  }

  previewMotd(): void {
    this.showMotdPreview.set(true);
  }

  motdLines(): string[] {
    return (this.config.motd || '').split('\n');
  }

  closeMotdPreview(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal')) {
      this.showMotdPreview.set(false);
    }
  }
}
