import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, Plus, Trash2, KeyRound, Check, ShieldCheck, X } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import {
  UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent,
} from '../../shared/ui';

interface AdminUser {
  id: string;
  email: string;
  isAdmin: boolean;
  createdAtUtc: string;
  accountCount: number;
}

/**
 * Users — admin-only management page. Lists every user with role, account count and
 * created date; supports adding a user (email/password/admin), resetting a password,
 * and deleting a user (cascade-removes all their data). The current user's own row
 * cannot be deleted (backend also guards this, plus self/last-admin rules).
 */
@Component({
  selector: 'ph-users',
  standalone: true,
  imports: [
    FormsModule, DatePipe, LucideAngularModule,
    UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent,
  ],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <!-- Header -->
      <div class="flex items-center justify-between gap-4">
        <div>
          <h1 class="text-xl font-semibold tracking-tight">Users</h1>
          <p class="text-sm text-text-muted mt-0.5">Manage who can access Profit Hub. Admins manage all users.</p>
        </div>
        <button uiButton variant="primary" (click)="openAdd()">
          <lucide-icon [img]="icons.Plus" class="h-4 w-4"></lucide-icon>
          Add user
        </button>
      </div>

      <!-- Users table -->
      <ui-card [padded]="false">
        @if (loading()) {
          <ui-spinner label="Loading users…" />
        } @else {
        <ui-table dense>
          <table>
            <thead>
              <tr>
                <th>Email</th>
                <th>Role</th>
                <th class="!text-right">Accounts</th>
                <th class="!text-right">Created</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (u of rows(); track u.id) {
                <tr>
                  <td class="font-medium text-text">{{ u.email }}</td>
                  <td>
                    @if (u.isAdmin) {
                      <ui-badge variant="brand">
                        <lucide-icon [img]="icons.ShieldCheck" class="h-3 w-3"></lucide-icon>
                        Admin
                      </ui-badge>
                    } @else {
                      <ui-badge variant="neutral">User</ui-badge>
                    }
                  </td>
                  <td class="text-right tabular-nums text-text-muted">{{ u.accountCount }}</td>
                  <td class="text-right text-text-muted tabular-nums">{{ u.createdAtUtc | date:'mediumDate' }}</td>
                  <td class="text-right">
                    <div class="flex items-center justify-end gap-1">
                      <button
                        type="button"
                        title="Reset password"
                        class="grid h-7 w-7 place-items-center rounded-md text-text-faint transition-colors hover:text-text hover:bg-surface-raised"
                        (click)="openReset(u)"
                      >
                        <lucide-icon [img]="icons.KeyRound" class="h-4 w-4"></lucide-icon>
                      </button>
                      @if (u.email !== currentEmail) {
                        <button
                          type="button"
                          title="Delete user"
                          class="grid h-7 w-7 place-items-center rounded-md text-text-faint transition-colors hover:text-loss hover:bg-loss/10"
                          (click)="remove(u)"
                        >
                          <lucide-icon [img]="icons.Trash2" class="h-4 w-4"></lucide-icon>
                        </button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="!py-10 text-center text-sm text-text-faint">
                    No users yet.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </ui-table>
        }
      </ui-card>
    </div>

    <!-- Saved toast -->
    @if (toast()) {
      <div class="fixed bottom-6 left-1/2 -translate-x-1/2 z-50 animate-fade-in">
        <ui-badge variant="brand">
          <lucide-icon [img]="icons.Check" class="h-3.5 w-3.5"></lucide-icon>
          {{ toast() }}
        </ui-badge>
      </div>
    }

    <!-- Add user modal -->
    @if (showAdd()) {
      <div class="fixed inset-0 z-40 flex items-center justify-center px-4">
        <div class="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" (click)="showAdd.set(false)"></div>
        <div class="relative w-full max-w-md animate-fade-in">
          <ui-card [hasHeader]="true">
            <div uiCardHeader class="flex items-center justify-between">
              <h2 class="text-base font-semibold">Add user</h2>
              <button
                type="button"
                title="Close"
                class="grid h-7 w-7 place-items-center rounded-md text-text-faint transition-colors hover:text-text hover:bg-surface-raised"
                (click)="showAdd.set(false)"
              >
                <lucide-icon [img]="icons.X" class="h-4 w-4"></lucide-icon>
              </button>
            </div>

            <form class="flex flex-col gap-4" (ngSubmit)="add()">
              <label class="flex flex-col gap-1.5">
                <span class="text-xs font-medium text-text-muted">Email</span>
                <input
                  [(ngModel)]="email"
                  name="email"
                  type="email"
                  autocomplete="off"
                  placeholder="you@example.com"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>
              <label class="flex flex-col gap-1.5">
                <span class="text-xs font-medium text-text-muted">Password</span>
                <input
                  [(ngModel)]="password"
                  name="password"
                  type="password"
                  autocomplete="new-password"
                  placeholder="At least 8 characters"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>
              <label class="flex items-center gap-2.5 select-none cursor-pointer">
                <input
                  [(ngModel)]="isAdmin"
                  name="isAdmin"
                  type="checkbox"
                  class="h-4 w-4 rounded border-border bg-surface-raised text-brand-500 focus:ring-brand-500/30"
                />
                <span class="text-sm text-text">Admin (full user management access)</span>
              </label>

              @if (addError()) {
                <div class="text-sm text-loss">{{ addError() }}</div>
              }

              <div class="flex justify-end gap-2.5 mt-1">
                <button uiButton variant="ghost" type="button" (click)="showAdd.set(false)">Cancel</button>
                <button uiButton variant="primary" type="submit">
                  <lucide-icon [img]="icons.Plus" class="h-4 w-4"></lucide-icon>
                  Add user
                </button>
              </div>
            </form>
          </ui-card>
        </div>
      </div>
    }

    <!-- Reset password modal -->
    @if (resetTarget()) {
      <div class="fixed inset-0 z-40 flex items-center justify-center px-4">
        <div class="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" (click)="resetTarget.set(null)"></div>
        <div class="relative w-full max-w-md animate-fade-in">
          <ui-card [hasHeader]="true">
            <div uiCardHeader class="flex items-center justify-between">
              <h2 class="text-base font-semibold">Reset password</h2>
              <button
                type="button"
                title="Close"
                class="grid h-7 w-7 place-items-center rounded-md text-text-faint transition-colors hover:text-text hover:bg-surface-raised"
                (click)="resetTarget.set(null)"
              >
                <lucide-icon [img]="icons.X" class="h-4 w-4"></lucide-icon>
              </button>
            </div>

            <form class="flex flex-col gap-4" (ngSubmit)="resetPassword()">
              <p class="text-sm text-text-muted">Set a new password for <span class="text-text font-medium">{{ resetTarget()!.email }}</span>.</p>
              <label class="flex flex-col gap-1.5">
                <span class="text-xs font-medium text-text-muted">New password</span>
                <input
                  [(ngModel)]="newPassword"
                  name="newPassword"
                  type="password"
                  autocomplete="new-password"
                  placeholder="At least 8 characters"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>

              @if (resetError()) {
                <div class="text-sm text-loss">{{ resetError() }}</div>
              }

              <div class="flex justify-end gap-2.5 mt-1">
                <button uiButton variant="ghost" type="button" (click)="resetTarget.set(null)">Cancel</button>
                <button uiButton variant="primary" type="submit">
                  <lucide-icon [img]="icons.KeyRound" class="h-4 w-4"></lucide-icon>
                  Save password
                </button>
              </div>
            </form>
          </ui-card>
        </div>
      </div>
    }
  `,
})
export class UsersComponent implements OnInit {
  rows = signal<AdminUser[]>([]);
  loading = signal(true);
  toast = signal('');

  showAdd = signal(false);
  email = ''; password = ''; isAdmin = false;
  addError = signal('');

  resetTarget = signal<AdminUser | null>(null);
  newPassword = '';
  resetError = signal('');

  readonly currentEmail: string;
  readonly icons = { Plus, Trash2, KeyRound, Check, ShieldCheck, X };

  constructor(private api: ApiService, auth: AuthService) {
    this.currentEmail = auth.email;
  }

  async ngOnInit() { await this.reload(); }

  async reload() {
    this.rows.set(await firstValueFrom(this.api.get<AdminUser[]>('/api/admin/users')));
    this.loading.set(false);
  }

  openAdd() {
    this.email = ''; this.password = ''; this.isAdmin = false;
    this.addError.set('');
    this.showAdd.set(true);
  }

  async add() {
    this.addError.set('');
    if (!this.email.trim() || this.password.length < 8) {
      this.addError.set('Email and a password of at least 8 characters are required.');
      return;
    }
    try {
      await firstValueFrom(this.api.post('/api/admin/users', {
        email: this.email.trim(), password: this.password, isAdmin: this.isAdmin,
      }));
      this.showAdd.set(false);
      this.flash('User added');
      await this.reload();
    } catch (e: unknown) {
      const status = (e as { status?: number })?.status;
      this.addError.set(status === 409 ? 'A user with that email already exists.' : 'Could not create user.');
    }
  }

  openReset(u: AdminUser) {
    this.newPassword = '';
    this.resetError.set('');
    this.resetTarget.set(u);
  }

  async resetPassword() {
    const target = this.resetTarget();
    if (!target) return;
    this.resetError.set('');
    if (this.newPassword.length < 8) {
      this.resetError.set('Password must be at least 8 characters.');
      return;
    }
    await firstValueFrom(this.api.put(`/api/admin/users/${target.id}/password`, { password: this.newPassword }));
    this.resetTarget.set(null);
    this.flash('Password reset');
  }

  async remove(u: AdminUser) {
    if (!confirm(`Delete ${u.email} and all of their accounts, trades and data? This cannot be undone.`)) return;
    try {
      await firstValueFrom(this.api.delete(`/api/admin/users/${u.id}`));
      this.flash('User deleted');
      await this.reload();
    } catch (e: unknown) {
      const msg = (e as { error?: { error?: string } })?.error?.error;
      alert(msg ? `Could not delete user: ${msg}` : 'Could not delete user.');
    }
  }

  private flash(msg: string) {
    this.toast.set(msg);
    setTimeout(() => this.toast.set(''), 1500);
  }
}
