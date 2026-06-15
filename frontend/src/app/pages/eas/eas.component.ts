import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, Bot, Check } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent } from '../../shared/ui';

interface Ea {
  magicNumber: number;
  name: string;        // saved EaName ('' if unnamed)
  accountName: string;
  netProfit: number;
  tradeCount: number;
}

/**
 * EAs management page — one row per magic number found in the user's trades, with
 * its owning account and lifetime stats. The name field is editable inline and
 * persisted via PUT /api/ea-names/{magic}; an empty name shows the magic number.
 */
@Component({
  selector: 'ph-eas',
  standalone: true,
  imports: [FormsModule, DecimalPipe, LucideAngularModule, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <header class="flex flex-col gap-1">
        <h1 class="text-xl font-semibold tracking-tight">EAs</h1>
        <p class="text-sm text-text-muted">Name each EA and review its performance. One row per magic number.</p>
      </header>

      <ui-card [padded]="false">
        @if (loading()) {
          <ui-spinner label="Loading EAs…" />
        } @else {
        <ui-table dense>
          <table>
            <thead>
              <tr>
                <th>EA</th>
                <th>Account</th>
                <th class="!text-right">Net</th>
                <th class="!text-right">Trades</th>
              </tr>
            </thead>
            <tbody>
              @for (r of rows(); track r.magicNumber) {
                <tr>
                  <td>
                    <div class="flex flex-col gap-0.5 py-1">
                      <input
                        [(ngModel)]="r.name"
                        (blur)="save(r)"
                        (keyup.enter)="save(r)"
                        [placeholder]="'#' + r.magicNumber"
                        class="h-8 w-48 max-w-full rounded-md border border-border bg-surface-raised px-2.5 text-sm text-text
                               placeholder:text-text-faint transition-colors hover:bg-border
                               focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                      />
                      <span class="text-[11px] tabular-nums text-text-faint">magic #{{ r.magicNumber }}</span>
                    </div>
                  </td>
                  <td class="text-text-muted">{{ r.accountName }}</td>
                  <td
                    class="text-right tabular-nums font-medium"
                    [class.text-profit]="r.netProfit >= 0"
                    [class.text-loss]="r.netProfit < 0"
                  >{{ r.netProfit | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ r.tradeCount }}</td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="4">
                    <div class="flex flex-col items-center gap-2 py-10 text-text-faint">
                      <lucide-icon [img]="icons.Bot" class="h-8 w-8"></lucide-icon>
                      <span class="text-sm">No EAs yet — they appear once trades are ingested.</span>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </ui-table>
        }
      </ui-card>
    </div>

    @if (saved()) {
      <div class="fixed bottom-5 left-1/2 z-50 -translate-x-1/2">
        <ui-badge variant="brand">
          <lucide-icon [img]="icons.Check" class="mr-1 inline h-3.5 w-3.5"></lucide-icon>
          Saved
        </ui-badge>
      </div>
    }
  `,
})
export class EasComponent implements OnInit {
  rows = signal<Ea[]>([]);
  saved = signal(false);
  loading = signal(true);
  readonly icons = { Bot, Check };

  constructor(private api: ApiService) {}

  async ngOnInit() {
    this.rows.set(await firstValueFrom(this.api.get<Ea[]>('/api/eas')));
    this.loading.set(false);
  }

  async save(r: Ea) {
    await firstValueFrom(this.api.put(`/api/ea-names/${r.magicNumber}`, { name: r.name.trim() }));
    this.saved.set(true);
    setTimeout(() => this.saved.set(false), 1500);
  }
}
