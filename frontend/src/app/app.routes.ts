import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', title: 'Sign in', loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent) },
  {
    path: '', canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', title: 'Dashboard', loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'trades', title: 'Trades', loadComponent: () => import('./pages/trades/trades.component').then(m => m.TradesComponent) },
      { path: 'accounts', title: 'Accounts', loadComponent: () => import('./pages/accounts/accounts.component').then(m => m.AccountsComponent) },
      { path: 'eas', title: 'EAs', loadComponent: () => import('./pages/eas/eas.component').then(m => m.EasComponent) },
      { path: 'settings', title: 'Settings', loadComponent: () => import('./pages/settings/settings.component').then(m => m.SettingsComponent) },
    ],
  },
];
