import { Component, Input, signal, computed } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { UiCardComponent } from '../../shared/ui';

interface HeatCell { year: number; month: number; netProfit: number; tradeCount: number; }
export interface HeatmapFilter { accountIds: string; magic: number | null; }

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

/** Monthly Net Profit heatmap — years (rows, newest first) × months, + a year total.
 *  USD only; cell tint encodes sign/magnitude. Respects account+EA filter, ignores date. */
@Component({
  selector: 'ph-monthly-heatmap',
  standalone: true,
  imports: [DecimalPipe, UiCardComponent],
  template: `
    <ui-card [hasHeader]="true">
      <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">Monthly performance (Net Profit, USD)</h2></div>
      <div class="overflow-x-auto px-2 pb-3 pt-3">
        @if (error()) {
          <div class="py-8 text-center text-sm text-loss">โหลด heatmap ไม่สำเร็จ <button class="ml-2 underline" (click)="reload()">ลองใหม่</button></div>
        } @else if (years().length === 0) {
          <div class="py-8 text-center text-sm text-text-faint">No trades yet.</div>
        } @else {
          <table class="w-full border-separate border-spacing-1 text-xs">
            <thead>
              <tr>
                <th class="px-2 py-1 text-left font-medium text-text-faint">Year</th>
                @for (m of months; track m) { <th class="px-1 py-1 text-center font-medium text-text-faint">{{ m }}</th> }
                <th class="px-2 py-1 text-right font-medium text-text-faint">Total</th>
              </tr>
            </thead>
            <tbody>
              @for (y of years(); track y) {
                <tr>
                  <td class="px-2 py-1 font-medium tabular-nums text-text-muted">{{ y }}</td>
                  @for (m of monthIdx; track m) {
                    <td class="h-9 w-12 rounded text-center tabular-nums"
                        [style.background]="cellBg(y, m)"
                        [title]="cellTitle(y, m)">
                      <span [class.text-text-faint]="value(y,m) === null">{{ value(y, m) !== null ? (value(y, m)! | number:'1.0-0') : '·' }}</span>
                    </td>
                  }
                  <td class="px-2 py-1 text-right font-semibold tabular-nums"
                      [class.text-profit]="yearTotal(y) >= 0" [class.text-loss]="yearTotal(y) < 0">{{ yearTotal(y) | number:'1.0-0' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </ui-card>
  `,
})
export class MonthlyHeatmapComponent {
  readonly months = MONTHS;
  readonly monthIdx = [1,2,3,4,5,6,7,8,9,10,11,12];
  private cells = signal<HeatCell[]>([]);
  error = signal(false);
  private gen = 0; // guards against out-of-order responses on rapid filter changes

  private _filter: HeatmapFilter = { accountIds: '', magic: null };
  @Input() set filter(f: HeatmapFilter) { this._filter = f; this.reload(); }

  constructor(private api: ApiService) {}

  async reload() {
    const myGen = ++this.gen;
    const params: Record<string, string> = {};
    if (this._filter.accountIds) params['accountIds'] = this._filter.accountIds;
    if (this._filter.magic !== null) params['magic'] = String(this._filter.magic);
    try {
      const data = await firstValueFrom(this.api.get<HeatCell[]>('/api/heatmap', params));
      if (myGen !== this.gen) return; // a newer request superseded this one
      this.cells.set(data);
      this.error.set(false);
    } catch {
      if (myGen !== this.gen) return;
      this.error.set(true);
    }
  }

  // O(1) lookups keyed by "year-month", rebuilt only when cells change.
  private cellMap = computed(() => new Map(this.cells().map(c => [`${c.year}-${c.month}`, c])));
  years = computed(() => [...new Set(this.cells().map(c => c.year))].sort((a, b) => b - a));
  private yearTotals = computed(() => {
    const m = new Map<number, number>();
    for (const c of this.cells()) m.set(c.year, (m.get(c.year) ?? 0) + c.netProfit);
    return m;
  });
  private cell(y: number, m: number) { return this.cellMap().get(`${y}-${m}`); }
  value(y: number, m: number): number | null { const c = this.cell(y, m); return c ? c.netProfit : null; }
  yearTotal(y: number) { return this.yearTotals().get(y) ?? 0; }
  cellTitle(y: number, m: number) { const c = this.cell(y, m); return c ? `${MONTHS[m-1]} ${y}: ${c.netProfit.toFixed(2)} (${c.tradeCount} trades)` : ''; }

  private maxAbs = computed(() => Math.max(1, ...this.cells().map(c => Math.abs(c.netProfit))));
  cellBg(y: number, m: number): string {
    const v = this.value(y, m);
    if (v === null) return 'transparent';
    const a = Math.min(0.85, 0.12 + 0.73 * Math.abs(v) / this.maxAbs());
    return v >= 0 ? `rgba(48,164,108,${a})` : `rgba(229,72,77,${a})`;
  }
}
