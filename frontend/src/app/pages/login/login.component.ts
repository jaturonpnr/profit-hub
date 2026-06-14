import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LucideAngularModule, TrendingUp, Mail, Lock, LogIn, UserPlus, AlertCircle } from 'lucide-angular';
import { AuthService } from '../../core/auth.service';
import { UiButtonComponent, UiCardComponent } from '../../shared/ui';

/**
 * Login — centered auth card on a subtle violet aurora dark background.
 * Presentation only: email/password fields, error signal, submit(register)
 * and navigation are preserved verbatim.
 */
@Component({
  selector: 'ph-login',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, UiButtonComponent, UiCardComponent],
  template: `
    <div class="relative min-h-screen flex items-center justify-center overflow-hidden bg-bg px-4">
      <!-- Aurora background -->
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -top-40 -left-32 h-[36rem] w-[36rem] rounded-full bg-brand-600/20 blur-[120px]"></div>
        <div class="absolute -bottom-48 -right-24 h-[32rem] w-[32rem] rounded-full bg-brand-800/25 blur-[120px]"></div>
        <div class="absolute top-1/3 left-1/2 h-72 w-72 -translate-x-1/2 rounded-full bg-brand-500/10 blur-[100px]"></div>
      </div>

      <!-- Auth card -->
      <div class="relative w-full max-w-sm animate-fade-in">
        <!-- Brand lockup -->
        <div class="mb-7 flex flex-col items-center gap-3">
          <div class="h-12 w-12 rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 grid place-items-center shadow-glow">
            <lucide-icon [img]="icons.TrendingUp" class="h-6 w-6 text-white"></lucide-icon>
          </div>
          <div class="text-center">
            <h1 class="text-xl font-semibold tracking-tight">Profit Hub</h1>
            <p class="text-sm text-text-muted mt-1">Sign in to your trading dashboard</p>
          </div>
        </div>

        <ui-card>
          <form class="flex flex-col gap-4" (ngSubmit)="submit(false)">
            <!-- Email -->
            <label class="flex flex-col gap-1.5">
              <span class="text-xs font-medium text-text-muted">Email</span>
              <div class="relative">
                <lucide-icon [img]="icons.Mail" class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-text-faint"></lucide-icon>
                <input
                  [(ngModel)]="email"
                  name="email"
                  type="email"
                  autocomplete="email"
                  placeholder="you@example.com"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border pl-10 pr-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </div>
            </label>

            <!-- Password -->
            <label class="flex flex-col gap-1.5">
              <span class="text-xs font-medium text-text-muted">Password</span>
              <div class="relative">
                <lucide-icon [img]="icons.Lock" class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-text-faint"></lucide-icon>
                <input
                  [(ngModel)]="password"
                  name="password"
                  type="password"
                  autocomplete="current-password"
                  placeholder="••••••••"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border pl-10 pr-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </div>
            </label>

            <!-- Inline error -->
            @if (error()) {
              <div class="flex items-center gap-2 rounded-md bg-loss/10 border border-loss/20 px-3 py-2 text-sm text-loss">
                <lucide-icon [img]="icons.AlertCircle" class="h-4 w-4 shrink-0"></lucide-icon>
                <span>{{ error() }}</span>
              </div>
            }

            <!-- Actions -->
            <div class="flex flex-col gap-2.5 mt-1">
              <button uiButton variant="primary" type="submit" [block]="true">
                <lucide-icon [img]="icons.LogIn" class="h-4 w-4"></lucide-icon>
                Sign in
              </button>
              <button uiButton variant="secondary" type="button" [block]="true" (click)="submit(true)">
                <lucide-icon [img]="icons.UserPlus" class="h-4 w-4"></lucide-icon>
                Register
              </button>
            </div>
          </form>
        </ui-card>
      </div>
    </div>
  `,
})
export class LoginComponent {
  email = ''; password = '';
  error = signal('');
  readonly icons = { TrendingUp, Mail, Lock, LogIn, UserPlus, AlertCircle };
  constructor(private auth: AuthService, private router: Router) {}
  async submit(register: boolean) {
    try {
      register ? await this.auth.register(this.email, this.password)
               : await this.auth.login(this.email, this.password);
      this.router.navigate(['/']);
    } catch { this.error.set(register ? 'Registration failed' : 'Wrong email or password'); }
  }
}
