import { Component, computed, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LucideAngularModule, LayoutDashboard, TrendingUp, Wallet, LogOut, Settings, Bot, ShieldCheck, FlaskConical, Banknote, Gauge } from 'lucide-angular';
import { AuthService } from '../core/auth.service';
import { UiButtonComponent } from '../shared/ui';

/**
 * App shell — fixed left sidebar (brand lockup + Lucide nav icons, violet
 * active state) and a content area with consistent padding + subtle app bg.
 * Logic preserved: RouterLink/RouterLinkActive/RouterOutlet and logout().
 */
@Component({
  selector: 'ph-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LucideAngularModule, UiButtonComponent],
  template: `
    <div class="min-h-screen flex bg-bg text-text">
      <!-- Sidebar -->
      <aside
        class="fixed inset-y-0 left-0 w-60 flex flex-col border-r border-border bg-surface/60 backdrop-blur-sm z-20"
      >
        <!-- Brand lockup -->
        <div class="h-16 flex items-center gap-2.5 px-5 border-b border-border-subtle">
          <div
            class="h-8 w-8 rounded-md bg-gradient-to-br from-brand-400 to-brand-700 grid place-items-center shadow-glow"
          >
            <lucide-icon [img]="icons.TrendingUp" class="h-4 w-4 text-white"></lucide-icon>
          </div>
          <span class="font-semibold tracking-tight text-[15px]">Profit Hub</span>
        </div>

        <!-- Nav -->
        <nav class="flex-1 px-3 py-4 flex flex-col gap-1">
          @for (item of navItems(); track item.path) {
            <a
              [routerLink]="item.path"
              [routerLinkActiveOptions]="{ exact: item.exact }"
              routerLinkActive="ph-nav-active"
              class="group relative flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium
                     text-text-muted transition-colors duration-150
                     hover:text-text hover:bg-surface-raised"
            >
              <span
                class="ph-nav-bar absolute left-0 top-1.5 bottom-1.5 w-0.5 rounded-full bg-brand-500 opacity-0 transition-opacity"
              ></span>
              <lucide-icon [img]="item.icon" class="h-4 w-4"></lucide-icon>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>

        <!-- User block -->
        <div class="border-t border-border-subtle p-3">
          <div class="flex items-center gap-3 px-2 py-2 mb-1">
            <div
              class="h-8 w-8 shrink-0 rounded-full bg-surface-raised border border-border grid place-items-center text-xs font-semibold text-brand-300"
            >
              {{ initial() }}
            </div>
            <div class="min-w-0">
              <div class="truncate text-xs text-text" [title]="email()">{{ email() }}</div>
              <div class="text-[11px] text-text-faint">Signed in</div>
            </div>
          </div>
          <button uiButton variant="ghost" [block]="true" (click)="logout()">
            <lucide-icon [img]="icons.LogOut" class="h-4 w-4"></lucide-icon>
            Sign out
          </button>
        </div>
      </aside>

      <!-- Content -->
      <main class="flex-1 ml-60 min-h-screen">
        <div class="mx-auto max-w-7xl px-8 py-8 animate-fade-in">
          <router-outlet />
        </div>
      </main>
    </div>
  `,
  styles: [`
    .ph-nav-active {
      color: var(--text);
      background: color-mix(in srgb, var(--brand-500) 12%, transparent);
    }
    .ph-nav-active .ph-nav-bar {
      opacity: 1;
    }
  `],
})
export class ShellComponent {
  readonly icons = { TrendingUp, LogOut };
  // Users is admin-only — shown only when the JWT carries isAdmin=true.
  readonly navItems = computed(() => [
    { path: '/', label: 'Dashboard', icon: LayoutDashboard, exact: true },
    { path: '/trades', label: 'Trades', icon: TrendingUp, exact: false },
    { path: '/accounts', label: 'Accounts', icon: Wallet, exact: false },
    { path: '/eas', label: 'EAs', icon: Bot, exact: false },
    { path: '/backtests', label: 'Backtests', icon: FlaskConical, exact: false },
    { path: '/withdrawals', label: 'ถอนเงิน', icon: Banknote, exact: false },
    { path: '/risk', label: 'Risk Level', icon: Gauge, exact: false },
    ...(this.auth.isAdmin ? [{ path: '/users', label: 'Users', icon: ShieldCheck, exact: false }] : []),
    { path: '/settings', label: 'Settings', icon: Settings, exact: false },
  ]);

  // Decode the email from the JWT in localStorage if available.
  private readonly _email = signal<string>(this.decodeEmail());
  readonly email = computed(() => this._email() || 'account@profit.hub');
  readonly initial = computed(() => (this.email()[0] || 'A').toUpperCase());

  constructor(private auth: AuthService, private router: Router) {}

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  private decodeEmail(): string {
    try {
      const token = localStorage.getItem('ph_token');
      if (!token) return '';
      const payload = JSON.parse(atob(token.split('.')[1] || ''));
      return payload.email || payload.sub || '';
    } catch {
      return '';
    }
  }
}
