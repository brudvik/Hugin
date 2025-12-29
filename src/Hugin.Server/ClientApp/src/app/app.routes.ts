// Hugin Admin Panel - App Routes
import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { setupGuard } from './core/guards/setup.guard';

export const routes: Routes = [
  {
    path: '',
    canActivate: [setupGuard],
    children: [
      {
        path: '',
        redirectTo: 'dashboard',
        pathMatch: 'full'
      },
      {
        path: 'login',
        loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent)
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
        canActivate: [authGuard]
      },
      {
        path: 'users',
        loadComponent: () => import('./features/users/users.component').then(m => m.UsersComponent),
        canActivate: [authGuard]
      },
      {
        path: 'channels',
        loadComponent: () => import('./features/channels/channels.component').then(m => m.ChannelsComponent),
        canActivate: [authGuard]
      },
      {
        path: 'operators',
        loadComponent: () => import('./features/operators/operators.component').then(m => m.OperatorsComponent),
        canActivate: [authGuard]
      },
      {
        path: 'bans',
        loadComponent: () => import('./features/bans/bans.component').then(m => m.BansComponent),
        canActivate: [authGuard]
      },
      {
        path: 'config',
        loadComponent: () => import('./features/config/config.component').then(m => m.ConfigComponent),
        canActivate: [authGuard]
      }
    ]
  },
  {
    path: 'setup',
    loadComponent: () => import('./features/setup/setup.component').then(m => m.SetupComponent)
  },
  {
    path: '**',
    redirectTo: 'dashboard'
  }
];
