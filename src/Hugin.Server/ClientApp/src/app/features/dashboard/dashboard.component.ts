// Hugin Admin Panel - Dashboard Component
import { Component, inject, signal, computed, OnInit, OnDestroy, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ApiService } from '@core/services/api.service';
import { SignalRService } from '@core/services/signalr.service';
import { ServerStatus, RealTimeStats } from '@core/models/api.models';

interface StatCard {
  title: string;
  value: string | number;
  icon: string;
  color: string;
  change?: string;
  changeType?: 'up' | 'down' | 'neutral';
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="dashboard">
      <!-- Header -->
      <div class="dashboard-header mb-4">
        <div>
          <h1>Dashboard</h1>
          <p class="text-muted mb-0">Oversikt over IRC-serveren</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline-secondary me-2" (click)="refresh()" [disabled]="loading()">
            <i class="fas fa-sync-alt" [class.fa-spin]="loading()"></i>
            <span class="d-none d-md-inline ms-2">Oppdater</span>
          </button>
          <div class="dropdown">
            <button class="btn btn-primary dropdown-toggle" type="button" data-bs-toggle="dropdown">
              <i class="fas fa-cog me-2"></i>
              Handlinger
            </button>
            <ul class="dropdown-menu dropdown-menu-dark dropdown-menu-end">
              <li>
                <button class="dropdown-item" (click)="reloadConfig()">
                  <i class="fas fa-redo me-2"></i>
                  Last inn konfigurasjon på nytt
                </button>
              </li>
              <li><hr class="dropdown-divider"></li>
              <li>
                <button class="dropdown-item text-warning" (click)="restartServer()">
                  <i class="fas fa-sync me-2"></i>
                  Restart server
                </button>
              </li>
              <li>
                <button class="dropdown-item text-danger" (click)="shutdownServer()">
                  <i class="fas fa-power-off me-2"></i>
                  Stopp server
                </button>
              </li>
            </ul>
          </div>
        </div>
      </div>

      <!-- Status Banner -->
      @if (status()) {
        <div class="status-banner mb-4" [class.online]="status()?.isRunning" [class.offline]="!status()?.isRunning">
          <div class="status-indicator">
            <span class="status-dot"></span>
            <span class="status-text">{{ status()?.isRunning ? 'Server kjører' : 'Server stoppet' }}</span>
          </div>
          <div class="status-info d-none d-md-flex">
            <span><i class="fas fa-clock me-1"></i> Oppetid: {{ formatUptime(status()?.uptime) }}</span>
            <span><i class="fas fa-server me-1"></i> {{ status()?.serverName }}</span>
            <span class="realtime-indicator" [class.connected]="isConnected()">
              <i class="fas fa-bolt me-1"></i>
              {{ isConnected() ? 'Sanntid' : 'Polling' }}
            </span>
          </div>
        </div>
      }

      <!-- Stats Grid -->
      <div class="row g-4 mb-4">
        @for (stat of stats(); track stat.title) {
          <div class="col-sm-6 col-xl-3">
            <div class="stat-card">
              <div class="stat-icon" [style.background]="stat.color">
                <i class="fas fa-{{ stat.icon }}"></i>
              </div>
              <div class="stat-content">
                <div class="stat-value">{{ stat.value }}</div>
                <div class="stat-title">{{ stat.title }}</div>
                @if (stat.change) {
                  <div class="stat-change" 
                       [class.text-success]="stat.changeType === 'up'"
                       [class.text-danger]="stat.changeType === 'down'">
                    <i class="fas fa-arrow-{{ stat.changeType === 'up' ? 'up' : 'down' }}"></i>
                    {{ stat.change }}
                  </div>
                }
              </div>
            </div>
          </div>
        }
      </div>

      <!-- Main Content Grid -->
      <div class="row g-4">
        <!-- Recent Activity -->
        <div class="col-lg-8">
          <div class="card h-100">
            <div class="card-header d-flex justify-content-between align-items-center">
              <h5 class="mb-0">
                <i class="fas fa-chart-line me-2"></i>
                Serveraktivitet
              </h5>
              <div class="btn-group btn-group-sm">
                <button class="btn btn-outline-secondary active">1t</button>
                <button class="btn btn-outline-secondary">24t</button>
                <button class="btn btn-outline-secondary">7d</button>
              </div>
            </div>
            <div class="card-body">
              <!-- Placeholder for chart -->
              <div class="chart-placeholder">
                <i class="fas fa-chart-area"></i>
                <p>Aktivitetsdiagram kommer snart</p>
              </div>
            </div>
          </div>
        </div>

        <!-- Quick Stats -->
        <div class="col-lg-4">
          <div class="card h-100">
            <div class="card-header">
              <h5 class="mb-0">
                <i class="fas fa-info-circle me-2"></i>
                Serverinfo
              </h5>
            </div>
            <div class="card-body">
              <dl class="info-list mb-0">
                <div class="info-item">
                  <dt>Versjon</dt>
                  <dd>{{ status()?.version || 'N/A' }}</dd>
                </div>
                <div class="info-item">
                  <dt>Nettverk</dt>
                  <dd>{{ status()?.networkName || 'N/A' }}</dd>
                </div>
                <div class="info-item">
                  <dt>TLS-port</dt>
                  <dd>{{ status()?.tlsPort || 'N/A' }}</dd>
                </div>
                <div class="info-item">
                  <dt>Maks tilkoblinger</dt>
                  <dd>{{ status()?.maxConnections || 'N/A' }}</dd>
                </div>
                <div class="info-item">
                  <dt>IRCv3 Capabilities</dt>
                  <dd>
                    @if (status()?.enabledCapabilities?.length) {
                      <span class="badge bg-primary me-1" 
                            *ngFor="let cap of status()?.enabledCapabilities?.slice(0, 3)">
                        {{ cap }}
                      </span>
                      @if ((status()?.enabledCapabilities?.length || 0) > 3) {
                        <span class="text-muted">+{{ (status()?.enabledCapabilities?.length || 0) - 3 }} mer</span>
                      }
                    } @else {
                      <span class="text-muted">Ingen</span>
                    }
                  </dd>
                </div>
              </dl>
            </div>
          </div>
        </div>
      </div>

      <!-- Recent Connections / Channels -->
      <div class="row g-4 mt-0">
        <!-- Active Channels -->
        <div class="col-lg-6">
          <div class="card">
            <div class="card-header d-flex justify-content-between align-items-center">
              <h5 class="mb-0">
                <i class="fas fa-hashtag me-2"></i>
                Aktive kanaler
              </h5>
              <a routerLink="/channels" class="btn btn-sm btn-outline-primary">
                Se alle
              </a>
            </div>
            <div class="card-body p-0">
              <div class="table-responsive">
                <table class="table table-hover mb-0">
                  <thead>
                    <tr>
                      <th>Kanal</th>
                      <th class="text-end">Brukere</th>
                      <th class="text-end">Meldinger</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (channel of topChannels(); track channel.name) {
                      <tr>
                        <td>
                          <i class="fas fa-hashtag text-muted me-1"></i>
                          {{ channel.name }}
                        </td>
                        <td class="text-end">{{ channel.userCount }}</td>
                        <td class="text-end">{{ channel.messageCount }}</td>
                      </tr>
                    } @empty {
                      <tr>
                        <td colspan="3" class="text-center text-muted py-4">
                          Ingen aktive kanaler
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>

        <!-- Recent Users -->
        <div class="col-lg-6">
          <div class="card">
            <div class="card-header d-flex justify-content-between align-items-center">
              <h5 class="mb-0">
                <i class="fas fa-users me-2"></i>
                Tilkoblede brukere
              </h5>
              <a routerLink="/users" class="btn btn-sm btn-outline-primary">
                Se alle
              </a>
            </div>
            <div class="card-body p-0">
              <div class="table-responsive">
                <table class="table table-hover mb-0">
                  <thead>
                    <tr>
                      <th>Bruker</th>
                      <th>Vert</th>
                      <th class="text-end">Tilkoblet</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (user of recentUsers(); track user.nickname) {
                      <tr>
                        <td>
                          <i class="fas fa-user text-muted me-1"></i>
                          {{ user.nickname }}
                        </td>
                        <td class="text-muted">{{ user.hostname }}</td>
                        <td class="text-end">{{ formatTime(user.connectedAt) }}</td>
                      </tr>
                    } @empty {
                      <tr>
                        <td colspan="3" class="text-center text-muted py-4">
                          Ingen tilkoblede brukere
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .dashboard-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: 1rem;
    }

    .dashboard-header h1 {
      font-size: 1.75rem;
      font-weight: 700;
      margin-bottom: 0.25rem;
    }

    .status-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 1rem 1.5rem;
      border-radius: 0.5rem;
      background: var(--card-bg);
      border: 1px solid var(--card-border);
    }

    .status-banner.online {
      border-left: 4px solid var(--bs-success);
    }

    .status-banner.offline {
      border-left: 4px solid var(--bs-danger);
    }

    .status-indicator {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .status-dot {
      width: 12px;
      height: 12px;
      border-radius: 50%;
      background: var(--bs-success);
      animation: pulse 2s infinite;
    }

    .status-banner.offline .status-dot {
      background: var(--bs-danger);
      animation: none;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .status-info {
      display: flex;
      gap: 2rem;
      color: var(--bs-gray-400);
      font-size: 0.875rem;
    }

    .realtime-indicator {
      padding: 2px 8px;
      border-radius: 4px;
      background: rgba(108, 117, 125, 0.2);
      color: var(--bs-gray-500);
    }

    .realtime-indicator.connected {
      background: rgba(16, 185, 129, 0.2);
      color: var(--bs-success);
    }
    }

    .stat-card {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 1.5rem;
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 0.75rem;
      transition: transform 0.2s ease, box-shadow 0.2s ease;
    }

    .stat-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    }

    .stat-icon {
      width: 56px;
      height: 56px;
      border-radius: 0.75rem;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 1.5rem;
      color: white;
      flex-shrink: 0;
    }

    .stat-value {
      font-size: 1.75rem;
      font-weight: 700;
      line-height: 1;
      margin-bottom: 0.25rem;
    }

    .stat-title {
      color: var(--bs-gray-400);
      font-size: 0.875rem;
    }

    .stat-change {
      font-size: 0.75rem;
      margin-top: 0.25rem;
    }

    .chart-placeholder {
      height: 250px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      color: var(--bs-gray-500);
    }

    .chart-placeholder i {
      font-size: 4rem;
      margin-bottom: 1rem;
      opacity: 0.3;
    }

    .info-list {
      display: flex;
      flex-direction: column;
    }

    .info-item {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      padding: 0.75rem 0;
      border-bottom: 1px solid var(--bs-border-color);
    }

    .info-item:last-child {
      border-bottom: none;
    }

    .info-item dt {
      color: var(--bs-gray-400);
      font-weight: 400;
      font-size: 0.875rem;
    }

    .info-item dd {
      margin: 0;
      text-align: right;
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
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly apiService = inject(ApiService);
  private readonly signalR = inject(SignalRService);
  private refreshInterval: any;

  loading = signal(false);
  status = signal<ServerStatus | null>(null);
  stats = signal<StatCard[]>([]);
  topChannels = signal<{ name: string; userCount: number; messageCount: number }[]>([]);
  recentUsers = signal<{ nickname: string; hostname: string; connectedAt: string }[]>([]);

  // Real-time stats from SignalR
  readonly realTimeStats = this.signalR.stats;
  readonly isConnected = this.signalR.isConnected;
  readonly connectionState = this.signalR.connectionState;

  // Computed live values that prefer real-time data when available
  readonly liveUsers = computed(() => this.realTimeStats()?.connectedUsers ?? this.status()?.connectedUsers ?? 0);
  readonly liveChannels = computed(() => this.realTimeStats()?.channelCount ?? this.status()?.channelCount ?? 0);
  readonly liveOperators = computed(() => this.realTimeStats()?.operatorsOnline ?? this.status()?.operatorsOnline ?? 0);
  readonly liveMemory = computed(() => this.realTimeStats()?.memoryUsageMb?.toFixed(1) ?? 'N/A');
  readonly liveCpu = computed(() => this.realTimeStats()?.cpuUsagePercent?.toFixed(1) ?? 'N/A');
  readonly liveMps = computed(() => this.realTimeStats()?.messagesPerSecond?.toFixed(1) ?? '0');

  constructor() {
    // Effect to update stats when real-time data changes
    effect(() => {
      const rtStats = this.realTimeStats();
      if (rtStats) {
        this.updateStatsFromRealTime(rtStats);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    this.refresh();
    
    // Connect to SignalR and subscribe to stats
    try {
      await this.signalR.connect();
      await this.signalR.subscribeToStats();
      await this.signalR.subscribeToUserEvents();
    } catch (error) {
      console.error('Failed to connect to real-time updates:', error);
      // Fallback to polling
      this.refreshInterval = setInterval(() => this.refresh(), 30000);
    }
  }

  async ngOnDestroy(): Promise<void> {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
    
    try {
      await this.signalR.unsubscribeFromStats();
      await this.signalR.unsubscribeFromUserEvents();
    } catch {
      // Ignore disconnect errors
    }
  }

  refresh(): void {
    this.loading.set(true);
    
    this.apiService.getStatus().subscribe({
      next: (status) => {
        this.status.set(status);
        this.updateStats(status);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });

    // Fetch top channels
    this.apiService.getChannels(1, 5).subscribe({
      next: (result) => {
        this.topChannels.set(result.items.map(c => ({
          name: c.name,
          userCount: c.userCount,
          messageCount: c.messageCount || 0
        })));
      }
    });

    // Fetch recent users
    this.apiService.getUsers(1, 5).subscribe({
      next: (result) => {
        this.recentUsers.set(result.items.map(u => ({
          nickname: u.nickname,
          hostname: u.hostname,
          connectedAt: u.connectedAt
        })));
      }
    });
  }

  private updateStats(status: ServerStatus): void {
    this.stats.set([
      {
        title: 'Tilkoblede brukere',
        value: status.connectedUsers,
        icon: 'users',
        color: 'linear-gradient(135deg, #6366f1, #8b5cf6)'
      },
      {
        title: 'Aktive kanaler',
        value: status.activeChannels,
        icon: 'hashtag',
        color: 'linear-gradient(135deg, #10b981, #059669)'
      },
      {
        title: 'Meldinger/sek',
        value: '0',
        icon: 'comment-dots',
        color: 'linear-gradient(135deg, #f59e0b, #d97706)'
      },
      {
        title: 'Minne (MB)',
        value: (status.statistics?.memoryUsageBytes || 0) / (1024 * 1024),
        icon: 'microchip',
        color: 'linear-gradient(135deg, #ec4899, #db2777)'
      }
    ]);
  }

  private updateStatsFromRealTime(rtStats: RealTimeStats): void {
    this.stats.set([
      {
        title: 'Tilkoblede brukere',
        value: rtStats.connectedUsers,
        icon: 'users',
        color: 'linear-gradient(135deg, #6366f1, #8b5cf6)'
      },
      {
        title: 'Aktive kanaler',
        value: rtStats.channelCount,
        icon: 'hashtag',
        color: 'linear-gradient(135deg, #10b981, #059669)'
      },
      {
        title: 'Meldinger/sek',
        value: rtStats.messagesPerSecond.toFixed(1),
        icon: 'comment-dots',
        color: 'linear-gradient(135deg, #f59e0b, #d97706)'
      },
      {
        title: 'Minne (MB)',
        value: rtStats.memoryUsageMb.toFixed(1),
        icon: 'microchip',
        color: 'linear-gradient(135deg, #ec4899, #db2777)'
      }
    ]);
  }

  formatUptime(seconds?: number): string {
    if (!seconds) return 'N/A';
    
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    
    if (days > 0) {
      return `${days}d ${hours}t`;
    } else if (hours > 0) {
      return `${hours}t ${minutes}m`;
    } else {
      return `${minutes}m`;
    }
  }

  formatTime(dateStr: string): string {
    if (!dateStr) return 'N/A';
    const date = new Date(dateStr);
    const now = new Date();
    const diff = Math.floor((now.getTime() - date.getTime()) / 1000);
    
    if (diff < 60) return 'Nå';
    if (diff < 3600) return `${Math.floor(diff / 60)}m siden`;
    if (diff < 86400) return `${Math.floor(diff / 3600)}t siden`;
    return `${Math.floor(diff / 86400)}d siden`;
  }

  formatNumber(num: number): string {
    if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
    if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
    return num.toString();
  }

  reloadConfig(): void {
    if (confirm('Er du sikker på at du vil laste inn konfigurasjonen på nytt?')) {
      this.apiService.reloadConfig().subscribe({
        next: () => {
          this.refresh();
        },
        error: (err) => {
          alert('Kunne ikke laste inn konfigurasjon: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  restartServer(): void {
    if (confirm('ADVARSEL: Dette vil koble fra alle brukere. Er du sikker?')) {
      this.apiService.restartServer().subscribe({
        next: () => {
          alert('Server starter på nytt...');
        },
        error: (err) => {
          alert('Kunne ikke restarte serveren: ' + (err.error?.message || 'Ukjent feil'));
        }
      });
    }
  }

  shutdownServer(): void {
    if (confirm('ADVARSEL: Dette vil stoppe serveren helt. Er du sikker?')) {
      if (confirm('Bekreft at du vil stoppe IRC-serveren.')) {
        this.apiService.shutdownServer().subscribe({
          next: () => {
            alert('Server stopper...');
          },
          error: (err) => {
            alert('Kunne ikke stoppe serveren: ' + (err.error?.message || 'Ukjent feil'));
          }
        });
      }
    }
  }
}
