import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import {
  LucideAngularModule, Download, FileDown, FileText, ChevronLeft, ChevronRight,
  ArrowUp, ArrowDown, Inbox,
} from 'lucide-angular';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';
import { UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent } from '../../shared/ui';

interface Trade {
  symbol: string; direction: string; lots: number; openPrice: number; closePrice: number;
  closeTimeUtc: string; netProfit: number; magicNumber: number; executionMs: number | null;
}

/**
 * Trades — terminal-style dense table inside a ui-card: BUY/SELL badges,
 * right-aligned tabular figures with profit/loss coloring, EA chip, styled
 * pager and export toolbar.
 *
 * Presentation only. All logic is preserved verbatim: load(page), pages(),
 * exportCsv(file, extra) blob-download, the trades/page/total signals,
 * summaryPeriod, the <ph-filter-bar (changed)="load(1)"> wiring, the field
 * names (symbol, direction, lots, openPrice, closePrice, closeTimeUtc,
 * netProfit, magicNumber), and the `closeTimeUtc | date:'short'` rendering.
 */
@Component({
  selector: 'ph-trades',
  standalone: true,
  imports: [
    FilterBarComponent, FormsModule, DatePipe, DecimalPipe, LucideAngularModule,
    UiButtonComponent, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent,
  ],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <div>
        <h1 class="text-xl font-semibold tracking-tight">Trades</h1>
        <p class="text-sm text-text-muted mt-0.5">Closed positions across your accounts.</p>
      </div>

      <!-- Toolbar: filter bar + export cluster -->
      <div class="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <ph-filter-bar (changed)="load(1)" />
        <div class="flex flex-wrap items-center gap-2">
          <select
            [(ngModel)]="summaryPeriod"
            class="h-9 rounded-md border border-border bg-surface-raised px-3 text-sm text-text
                   focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500
                   focus-visible:ring-offset-2 focus-visible:ring-offset-bg cursor-pointer"
            aria-label="Summary period"
          >
            <option value="day">Daily</option>
            <option value="week">Weekly</option>
            <option value="month">Monthly</option>
          </select>
          <button uiButton variant="primary" (click)="download('trades.csv', {})">
            <lucide-icon [img]="icons.Download" class="h-4 w-4"></lucide-icon>
            Export trades
          </button>
          <button uiButton variant="secondary" (click)="download('summary.csv', { period: summaryPeriod })">
            <lucide-icon [img]="icons.Download" class="h-4 w-4"></lucide-icon>
            Export summary
          </button>
          <button uiButton variant="secondary" (click)="download('workbook.xlsx', { period: summaryPeriod })">
            <lucide-icon [img]="icons.FileDown" class="h-4 w-4"></lucide-icon>
            Excel
          </button>
          <button uiButton variant="secondary" (click)="download('report.pdf', {})">
            <lucide-icon [img]="icons.FileText" class="h-4 w-4"></lucide-icon>
            PDF
          </button>
        </div>
      </div>

      <!-- Trades table -->
      <ui-card [padded]="false">
        @if (loading()) {
          <ui-spinner label="Loading trades…" />
        } @else if (total() === 0) {
          <div class="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <div class="flex h-12 w-12 items-center justify-center rounded-full bg-surface-raised border border-border">
              <lucide-icon [img]="icons.Inbox" class="h-5 w-5 text-text-faint"></lucide-icon>
            </div>
            <div>
              <p class="text-sm font-medium text-text-muted">No trades found</p>
              <p class="text-xs text-text-faint mt-0.5">Adjust your filters or wait for new closed positions.</p>
            </div>
          </div>
        } @else {
          <ui-table dense>
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Type</th>
                  <th class="!text-right">Lots</th>
                  <th class="!text-right">Open</th>
                  <th class="!text-right">Close</th>
                  <th class="!text-right">Profit</th>
                  <th class="!text-right" title="Server fill time (ORDER_TIME_DONE − SETUP). Approximate — not the terminal journal's 'done in X ms'.">Fill ≈ (ms)</th>
                  <th>EA</th>
                  <th>Closed</th>
                </tr>
              </thead>
              <tbody>
                @for (t of trades(); track $index) {
                  <tr>
                    <td class="font-medium">{{ t.symbol }}</td>
                    <td>
                      @if (t.direction === 'buy') {
                        <ui-badge variant="profit">
                          <lucide-icon [img]="icons.ArrowUp" class="h-3 w-3"></lucide-icon>
                          BUY
                        </ui-badge>
                      } @else {
                        <ui-badge variant="loss">
                          <lucide-icon [img]="icons.ArrowDown" class="h-3 w-3"></lucide-icon>
                          SELL
                        </ui-badge>
                      }
                    </td>
                    <td class="text-right tabular-nums text-text-muted">{{ t.lots }}</td>
                    <td class="text-right tabular-nums text-text-muted">{{ t.openPrice }}</td>
                    <td class="text-right tabular-nums text-text-muted">{{ t.closePrice }}</td>
                    <td
                      class="text-right tabular-nums font-medium"
                      [class.text-profit]="t.netProfit >= 0"
                      [class.text-loss]="t.netProfit < 0"
                    >{{ t.netProfit >= 0 ? '+' : '' }}{{ t.netProfit | number:'1.2-2' }}</td>
                    <td class="text-right tabular-nums text-text-muted">{{ t.executionMs != null ? (t.executionMs | number:'1.0-0') : '—' }}</td>
                    <td><ui-badge variant="neutral">{{ t.magicNumber }}</ui-badge></td>
                    <td class="tabular-nums text-text-muted whitespace-nowrap">{{ t.closeTimeUtc | date:'short' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </ui-table>
        }
      </ui-card>

      <!-- Pager -->
      <div class="flex items-center justify-center gap-3">
        <button uiButton variant="ghost" size="sm" [disabled]="page() <= 1" (click)="load(page() - 1)">
          <lucide-icon [img]="icons.ChevronLeft" class="h-4 w-4"></lucide-icon>
          Prev
        </button>
        <span class="text-sm text-text-muted tabular-nums">
          Page <span class="text-text font-medium">{{ page() }}</span> / {{ pages() }}
        </span>
        <button uiButton variant="ghost" size="sm" [disabled]="page() >= pages()" (click)="load(page() + 1)">
          Next
          <lucide-icon [img]="icons.ChevronRight" class="h-4 w-4"></lucide-icon>
        </button>
      </div>
    </div>
  `,
})
export class TradesComponent implements OnInit {
  readonly icons = { Download, FileDown, FileText, ChevronLeft, ChevronRight, ArrowUp, ArrowDown, Inbox };

  trades = signal<Trade[]>([]);
  page = signal(1); total = signal(0);
  loading = signal(true);
  summaryPeriod = 'day';
  constructor(private api: ApiService, private filter: FilterService, private http: HttpClient) {}

  pages() { return Math.max(1, Math.ceil(this.total() / 50)); }
  async ngOnInit() { await this.load(1); }
  async load(page: number) {
    this.page.set(page);
    const res = await firstValueFrom(this.api.get<{ total: number; items: Trade[] }>(
      '/api/trades', { ...this.filter.queryParams(), page: String(page) }));
    this.trades.set(res.items); this.total.set(res.total);
    this.loading.set(false);
  }
  /**
   * Blob-download any /api/export/* artifact (CSV, xlsx, PDF) with the current filters.
   * The auth interceptor is registered globally, so this HttpClient.get is sent with the
   * Bearer token even though we pass the full absolute URL (the export endpoints require
   * auth, and a token cannot be attached to a plain <a href> download).
   */
  async download(file: string, extra: Record<string, string>) {
    const params = new URLSearchParams({ ...this.filter.queryParams(), ...extra });
    const blob = await firstValueFrom(this.http.get(
      `${environment.apiUrl}/api/export/${file}?${params}`, { responseType: 'blob' }));
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob); a.download = file; a.click();
    URL.revokeObjectURL(a.href);
  }
}
