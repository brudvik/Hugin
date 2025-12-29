// Hugin Admin Panel - Sidebar Navigation Component
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '@core/services/auth.service';

interface NavItem {
  icon: string;
  label: string;
  route: string;
  adminOnly?: boolean;
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <aside class="sidebar d-flex flex-column">
      <!-- Brand -->
      <div class="sidebar-brand">
        <i class="fas fa-crow me-2"></i>
        <span>Hugin</span>
      </div>

      <!-- Navigation -->
      <nav class="sidebar-nav flex-grow-1">
        <ul class="nav flex-column">
          @for (item of navItems; track item.route) {
            @if (!item.adminOnly || isAdmin()) {
              <li class="nav-item">
                <a class="nav-link" 
                   [routerLink]="item.route" 
                   routerLinkActive="active"
                   [routerLinkActiveOptions]="{ exact: item.route === '/dashboard' }">
                  <i class="fas fa-{{ item.icon }} me-2"></i>
                  {{ item.label }}
                </a>
              </li>
            }
          }
        </ul>
      </nav>

      <!-- Server Status -->
      <div class="sidebar-status">
        <div class="d-flex align-items-center mb-2">
          <span class="status-dot online me-2"></span>
          <small class="text-muted">Server Online</small>
        </div>
      </div>

      <!-- User Menu -->
      <div class="sidebar-footer">
        <div class="dropdown">
          <button class="btn btn-dark w-100 d-flex align-items-center justify-content-between"
                  type="button"
                  data-bs-toggle="dropdown"
                  aria-expanded="false">
            <span>
              <i class="fas fa-user-circle me-2"></i>
              {{ currentUser()?.username || 'Admin' }}
            </span>
            <i class="fas fa-chevron-up"></i>
          </button>
          <ul class="dropdown-menu dropdown-menu-dark w-100">
            <li>
              <a class="dropdown-item" routerLink="/profile">
                <i class="fas fa-user me-2"></i>
                Profil
              </a>
            </li>
            <li><hr class="dropdown-divider"></li>
            <li>
              <button class="dropdown-item text-danger" (click)="logout()">
                <i class="fas fa-sign-out-alt me-2"></i>
                Logg ut
              </button>
            </li>
          </ul>
        </div>
      </div>
    </aside>
  `,
  styles: [`
    :host {
      display: block;
      height: 100vh;
    }

    .sidebar {
      width: 260px;
      height: 100%;
      background: var(--bs-dark);
      border-right: 1px solid var(--bs-border-color);
    }

    .sidebar-brand {
      padding: 1.5rem;
      font-size: 1.5rem;
      font-weight: 700;
      color: var(--bs-primary);
      border-bottom: 1px solid var(--bs-border-color);
    }

    .sidebar-nav {
      padding: 1rem 0;
      overflow-y: auto;
    }

    .sidebar-nav .nav-link {
      padding: 0.75rem 1.5rem;
      color: var(--bs-gray-400);
      transition: all 0.2s ease;
      border-left: 3px solid transparent;
    }

    .sidebar-nav .nav-link:hover {
      color: var(--bs-light);
      background: rgba(255, 255, 255, 0.05);
    }

    .sidebar-nav .nav-link.active {
      color: var(--bs-primary);
      background: rgba(var(--bs-primary-rgb), 0.1);
      border-left-color: var(--bs-primary);
    }

    .sidebar-status {
      padding: 1rem 1.5rem;
      border-top: 1px solid var(--bs-border-color);
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      display: inline-block;
    }

    .status-dot.online {
      background: var(--bs-success);
      box-shadow: 0 0 8px var(--bs-success);
    }

    .status-dot.offline {
      background: var(--bs-danger);
    }

    .sidebar-footer {
      padding: 1rem;
      border-top: 1px solid var(--bs-border-color);
    }

    .dropdown-menu {
      margin-bottom: 0.5rem !important;
    }
  `]
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly currentUser = this.authService.currentUser;
  readonly isAdmin = this.authService.isAdmin;

  navItems: NavItem[] = [
    { icon: 'tachometer-alt', label: 'Dashboard', route: '/dashboard' },
    { icon: 'users', label: 'Brukere', route: '/users' },
    { icon: 'hashtag', label: 'Kanaler', route: '/channels' },
    { icon: 'user-shield', label: 'Operatører', route: '/operators', adminOnly: true },
    { icon: 'ban', label: 'Utestengelser', route: '/bans', adminOnly: true },
    { icon: 'cog', label: 'Konfigurasjon', route: '/config', adminOnly: true }
  ];

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
