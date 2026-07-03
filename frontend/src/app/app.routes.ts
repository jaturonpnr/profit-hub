import { Routes } from '@angular/router';
import { authGuard, adminGuard } from './core/auth.guard';

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
      { path: 'withdrawals', title: 'ถอนเงิน', loadComponent: () => import('./pages/withdrawals/withdrawals.component').then(m => m.WithdrawalsComponent) },
      { path: 'risk', title: 'Risk Level', loadComponent: () => import('./pages/risk/risk.component').then(m => m.RiskComponent) },
      { path: 'eas/:magic', title: 'EA', loadComponent: () => import('./pages/eas/ea-detail.component').then(m => m.EaDetailComponent) },
      { path: 'backtests', title: 'Backtests', loadComponent: () => import('./pages/backtests/backtests.component').then(m => m.BacktestsComponent) },
      { path: 'backtests/:id', title: 'Backtest', loadComponent: () => import('./pages/backtests/backtest-detail.component').then(m => m.BacktestDetailComponent) },
      { path: 'users', title: 'Users', canActivate: [adminGuard], loadComponent: () => import('./pages/users/users.component').then(m => m.UsersComponent) },
      { path: 'settings', title: 'Settings', loadComponent: () => import('./pages/settings/settings.component').then(m => m.SettingsComponent) },
    ],
  },
];
