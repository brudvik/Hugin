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
        <img src="hugin-logo.png" alt="Hugin" style="width: 32px; height: 32px;" class="me-2">
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
      background: #181818;
      border-right: 1px solid #2d2d2d;
    }

    .sidebar-brand {
      padding: 1rem 1.25rem;
      display: flex;
      align-items: center;
      font-size: 1.25rem;
      font-weight: 400;
      color: #0078d4;
      border-bottom: 1px solid #2d2d2d;
    }

    .sidebar-nav {
      padding: 0.5rem 0;
      overflow-y: auto;
    }

    .sidebar-nav .nav-link {
      display: flex;
      align-items: center;
      padding: 0.625rem 1.25rem;
      color: #cccccc;
      transition: all 0.1s ease;
      border-left: 2px solid transparent;
      font-size: 0.8125rem;
    }

    .sidebar-nav .nav-link i {
      width: 20px;
      text-align: center;
      font-size: 0.875rem;
    }

    .sidebar-nav .nav-link:hover {
      color: #ffffff;
      background: rgba(255, 255, 255, 0.05);
    }

    .sidebar-nav .nav-link.active {
      color: #ffffff;
      background: #094771;
      border-left-color: #0078d4;
    }

    .sidebar-status {
      padding: 0.75rem 1.25rem;
      border-top: 1px solid #2d2d2d;
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      display: inline-block;
    }

    .status-dot.online {
      background: #89d185;
      box-shadow: 0 0 6px #89d185;
    }

    .status-dot.offline {
      background: #f14c4c;
    }

    .sidebar-footer {
      padding: 0.75rem;
      border-top: 1px solid #2d2d2d;
    }

    .sidebar-footer .btn {
      background: #2d2d2d;
      border-color: #2d2d2d;
      color: #cccccc;
      font-size: 0.8125rem;
    }

    .sidebar-footer .btn:hover {
      background: #3c3c3c;
      border-color: #3c3c3c;
    }

    .dropdown-menu {
      margin-bottom: 0.5rem !important;
      background: #3c3c3c;
      border-color: #454545;
    }

    .dropdown-item {
      color: #cccccc;
      font-size: 0.8125rem;
    }

    .dropdown-item:hover {
      background: #094771;
      color: #ffffff;
    }
  `]
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly currentUser = this.authService.user;
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
