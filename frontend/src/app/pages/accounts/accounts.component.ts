import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  LucideAngularModule, Plus, Trash2, Copy, Check, Eye, EyeOff,
  Wifi, WifiOff, Clock, X,
} from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { FilterService, AccountInfo } from '../../core/filter.service';
import { UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent } from '../../shared/ui';

type EaStatus = 'live' | 'stale' | 'never';

/**
 * Accounts — refined dense table: name + MT5 number, broker, currency,
 * masked ingest key with reveal + copy, and an EA heartbeat status pill.
 * Add-account moved into a lightweight Tailwind modal.
 *
 * Presentation only: reload(), add(), remove() (with confirm), copy(),
 * isStale(), filter.accounts() usage and all field names are preserved.
 */
@Component({
  selector: 'ph-accounts',
  standalone: true,
  imports: [FormsModule, DatePipe, LucideAngularModule, UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <!-- Header -->
      <div class="flex items-center justify-between gap-4">
        <div>
          <h1 class="text-xl font-semibold tracking-tight">Accounts</h1>
          <p class="text-sm text-text-muted mt-0.5">Manage your MT5 accounts and EA ingest keys.</p>
        </div>
        <button uiButton variant="primary" (click)="showAdd.set(true)">
          <lucide-icon [img]="icons.Plus" class="h-4 w-4"></lucide-icon>
          Add account
        </button>
      </div>

      <!-- Accounts table -->
      <ui-table dense>
        <table>
          <thead>
            <tr>
              <th>Account</th>
              <th>Broker</th>
              <th>Currency</th>
              <th>Ingest Key</th>
              <th>EA Status</th>
              <th class="!text-right">Last data</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (a of filter.accounts(); track a.id) {
              <tr>
                <td>
                  <div class="font-medium text-text">{{ a.name }}</div>
                  <div class="text-xs text-text-faint tabular-nums">#{{ a.accountNumber }}</div>
                </td>
                <td class="text-text-muted">{{ a.broker }}</td>
                <td class="text-text-muted">{{ a.currency }}</td>
                <td>
                  <div class="flex items-center gap-1.5">
                    <code class="text-xs text-text-muted tabular-nums">{{ revealed().has(a.id) ? a.ingestKey : mask(a.ingestKey) }}</code>
                    <button
                      type="button"
                      title="Reveal key"
                      class="grid h-6 w-6 place-items-center rounded text-text-faint transition-colors hover:text-text hover:bg-surface-raised"
                      (click)="toggleReveal(a.id)"
                    >
                      <lucide-icon [img]="revealed().has(a.id) ? icons.EyeOff : icons.Eye" class="h-3.5 w-3.5"></lucide-icon>
                    </button>
                    <button
                      type="button"
                      title="Copy key"
                      class="grid h-6 w-6 place-items-center rounded text-text-faint transition-colors hover:text-text hover:bg-surface-raised"
                      (click)="copy(a.ingestKey)"
                    >
                      <lucide-icon [img]="icons.Copy" class="h-3.5 w-3.5"></lucide-icon>
                    </button>
                  </div>
                </td>
                <td>
                  @switch (status(a)) {
                    @case ('live') {
                      <ui-badge variant="profit">
                        <lucide-icon [img]="icons.Wifi" class="h-3 w-3"></lucide-icon>
                        Live
                      </ui-badge>
                    }
                    @case ('stale') {
                      <ui-badge variant="amber">
                        <lucide-icon [img]="icons.Clock" class="h-3 w-3"></lucide-icon>
                        Stale
                      </ui-badge>
                    }
                    @default {
                      <ui-badge variant="neutral">
                        <lucide-icon [img]="icons.WifiOff" class="h-3 w-3"></lucide-icon>
                        Never
                      </ui-badge>
                    }
                  }
                </td>
                <td class="text-right text-text-muted tabular-nums">
                  {{ a.lastIngestAtUtc ? (a.lastIngestAtUtc | date:'short') : '—' }}
                </td>
                <td class="text-right">
                  <button
                    type="button"
                    title="Delete account"
                    class="grid h-7 w-7 place-items-center rounded-md text-text-faint transition-colors hover:text-loss hover:bg-loss/10"
                    (click)="remove(a)"
                  >
                    <lucide-icon [img]="icons.Trash2" class="h-4 w-4"></lucide-icon>
                  </button>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="7" class="!py-10 text-center text-sm text-text-faint">
                  No accounts yet. Click “Add account” to get started.
                </td>
              </tr>
            }
          </tbody>
        </table>
      </ui-table>
    </div>

    <!-- Copied toast -->
    @if (copied()) {
      <div class="fixed bottom-6 left-1/2 -translate-x-1/2 z-50 animate-fade-in">
        <ui-badge variant="brand">
          <lucide-icon [img]="icons.Check" class="h-3.5 w-3.5"></lucide-icon>
          Copied!
        </ui-badge>
      </div>
    }

    <!-- Add account modal -->
    @if (showAdd()) {
      <div class="fixed inset-0 z-40 flex items-center justify-center px-4">
        <div class="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" (click)="showAdd.set(false)"></div>
        <div class="relative w-full max-w-md animate-fade-in">
          <ui-card [hasHeader]="true">
            <div uiCardHeader class="flex items-center justify-between">
              <h2 class="text-base font-semibold">Add account</h2>
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
                <span class="text-xs font-medium text-text-muted">MT5 account number</span>
                <input
                  [(ngModel)]="num"
                  name="num"
                  type="number"
                  placeholder="12345678"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text tabular-nums
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>
              <label class="flex flex-col gap-1.5">
                <span class="text-xs font-medium text-text-muted">Name</span>
                <input
                  [(ngModel)]="name"
                  name="name"
                  placeholder="My live account"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>
              <label class="flex flex-col gap-1.5">
                <span class="text-xs font-medium text-text-muted">Broker</span>
                <input
                  [(ngModel)]="broker"
                  name="broker"
                  placeholder="IC Markets"
                  class="w-full h-10 rounded-md bg-surface-raised border border-border px-3 text-sm text-text
                         placeholder:text-text-faint transition-colors
                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </label>

              <div class="flex justify-end gap-2.5 mt-1">
                <button uiButton variant="ghost" type="button" (click)="showAdd.set(false)">Cancel</button>
                <button uiButton variant="primary" type="submit">
                  <lucide-icon [img]="icons.Plus" class="h-4 w-4"></lucide-icon>
                  Add account
                </button>
              </div>
            </form>
          </ui-card>
        </div>
      </div>
    }
  `,
})
export class AccountsComponent implements OnInit {
  num = 0; name = ''; broker = '';
  copied = signal(false);
  showAdd = signal(false);
  revealed = signal(new Set<string>());
  readonly icons = { Plus, Trash2, Copy, Check, Eye, EyeOff, Wifi, WifiOff, Clock, X };

  constructor(private api: ApiService, public filter: FilterService) {}

  async ngOnInit() { await this.reload(); }
  async reload() {
    this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
  }
  async add() {
    await firstValueFrom(this.api.post('/api/accounts', { accountNumber: this.num, name: this.name, broker: this.broker }));
    this.num = 0; this.name = ''; this.broker = '';
    this.showAdd.set(false);
    await this.reload();
  }
  async remove(a: AccountInfo) {
    if (!confirm(`Delete account ${a.accountNumber} and all its trades?`)) return;
    await firstValueFrom(this.api.delete(`/api/accounts/${a.id}`));
    await this.reload();
  }
  copy(key: string) { navigator.clipboard.writeText(key); this.copied.set(true); setTimeout(() => this.copied.set(false), 1500); }
  isStale(a: AccountInfo) { return !a.lastIngestAtUtc || Date.now() - Date.parse(a.lastIngestAtUtc) > 15 * 60_000; }

  /** EA heartbeat: live (<15m) / stale (older) / never (no data). */
  status(a: AccountInfo): EaStatus {
    if (!a.lastIngestAtUtc) return 'never';
    return this.isStale(a) ? 'stale' : 'live';
  }

  mask(key: string) { return key.slice(0, 4) + '••••••••'; }

  toggleReveal(id: string) {
    const next = new Set(this.revealed());
    next.has(id) ? next.delete(id) : next.add(id);
    this.revealed.set(next);
  }
}
