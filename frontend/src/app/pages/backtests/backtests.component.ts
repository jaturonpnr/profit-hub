import { Component, OnInit, signal, computed } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  NgApexchartsModule, ApexChart, ApexAxisChartSeries, ApexStroke,
  ApexDataLabels, ApexGrid, ApexXAxis, ApexYAxis, ApexTooltip, ApexLegend,
} from 'ng-apexcharts';
import { LucideAngularModule, FlaskConical, Upload, Trash2, GitCompare } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent, UiButtonComponent } from '../../shared/ui';

export interface BacktestSummary {
  id: string;
  expertName: string;
  symbol: string;
  timeframe: string;
  periodFrom: string | null;
  periodTo: string | null;
  magicNumber: number | null;
  initialDeposit: number;
  currency: string;
  netProfit: number;
  returnPct: number;
  profitFactor: number;
  recoveryFactor: number;
  sharpeRatio: number;
  balanceDrawdownMaxPct: number;
  equityDrawdownMaxPct: number;
  totalTrades: number;
  winRatePct: number;
  sourceFileName: string;
  createdAtUtc: string;
}

interface EquityPoint { t: string; balance: number; }

/**
 * Backtests page — the list IS the side-by-side comparison table (sortable KPI
 * columns). Select >=2 rows to overlay their equity curves (as Backtest Return %)
 * in one chart. Upload an MT5 .xlsx to add a Backtest. Isolated from live data.
 */
@Component({
  selector: 'ph-backtests',
  standalone: true,
  imports: [
    DecimalPipe, DatePipe, RouterLink, NgApexchartsModule, LucideAngularModule,
    UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent, UiButtonComponent,
  ],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <header class="flex items-start justify-between gap-4">
        <div class="flex flex-col gap-1">
          <h1 class="text-xl font-semibold tracking-tight">Backtests</h1>
          <p class="text-sm text-text-muted">Upload MT5 Strategy Tester reports and compare EAs. Hypothetical results — separate from live performance.</p>
        </div>
        <div>
          <input #fileInput type="file" accept=".xlsx" class="hidden" (change)="onFile($event)" />
          <button uiButton variant="primary" [disabled]="uploading()" (click)="fileInput.click()">
            <lucide-icon [img]="icons.Upload" class="h-4 w-4"></lucide-icon>
            {{ uploading() ? 'Uploading…' : 'Upload .xlsx' }}
          </button>
        </div>
      </header>

      @if (error()) {
        <ui-badge variant="loss">{{ error() }}</ui-badge>
      }

      @if (selectedIds().length >= 2) {
        <ui-card [hasHeader]="true">
          <div uiCardHeader class="flex items-center gap-2">
            <lucide-icon [img]="icons.GitCompare" class="h-4 w-4 text-brand-300"></lucide-icon>
            <h2 class="text-sm font-medium text-text-muted">Equity curve overlay — Backtest Return %</h2>
          </div>
          <div class="px-2 pb-2 pt-4">
            <apx-chart
              [series]="overlaySeries()" [chart]="chart" [stroke]="stroke"
              [dataLabels]="dataLabels" [grid]="grid" [xaxis]="xaxis" [yaxis]="yaxis"
              [tooltip]="tooltip" [legend]="legend"
            ></apx-chart>
          </div>
        </ui-card>
      }

      <ui-card [padded]="false">
        @if (loading()) {
          <ui-spinner label="Loading backtests…" />
        } @else {
        <ui-table>
          <table>
            <thead>
              <tr>
                <th class="!text-center">Compare</th>
                <th>Expert</th>
                <th>Symbol / TF</th>
                <th>Period</th>
                <th class="!text-right">Deposit</th>
                <th class="!text-right">Net Profit</th>
                <th class="!text-right">Return %</th>
                <th class="!text-right">PF</th>
                <th class="!text-right">Max Equity DD %</th>
                <th class="!text-right">Recovery</th>
                <th class="!text-right">Sharpe</th>
                <th class="!text-right">Trades</th>
                <th class="!text-right">Win %</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (b of rows(); track b.id) {
                <tr>
                  <td class="text-center">
                    <input type="checkbox" [checked]="isSelected(b.id)" (change)="toggle(b.id)"
                           class="h-4 w-4 rounded border-border bg-surface-raised accent-brand-500" />
                  </td>
                  <td>
                    <a [routerLink]="['/backtests', b.id]" class="font-medium text-text hover:text-brand-300 transition-colors">{{ b.expertName }}</a>
                    <div class="text-[11px] text-text-faint">{{ b.sourceFileName }}</div>
                  </td>
                  <td class="text-text-muted">{{ b.symbol }} <span class="text-text-faint">{{ b.timeframe }}</span></td>
                  <td class="text-text-muted text-xs tabular-nums">{{ b.periodFrom | date:'yyyy-MM-dd' }} → {{ b.periodTo | date:'yyyy-MM-dd' }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.initialDeposit | number:'1.0-0' }}</td>
                  <td class="text-right tabular-nums font-medium" [class.text-profit]="b.netProfit >= 0" [class.text-loss]="b.netProfit < 0">{{ b.netProfit | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums font-semibold" [class.text-profit]="b.returnPct >= 0" [class.text-loss]="b.returnPct < 0">{{ b.returnPct | number:'1.1-1' }}%</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.profitFactor | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums font-medium text-loss">{{ b.equityDrawdownMaxPct | number:'1.1-1' }}%</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.recoveryFactor | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.sharpeRatio | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.totalTrades }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ b.winRatePct | number:'1.1-1' }}%</td>
                  <td class="text-right">
                    <button (click)="remove(b)" class="text-text-faint hover:text-loss transition-colors p-1" title="Delete" aria-label="Delete backtest">
                      <lucide-icon [img]="icons.Trash2" class="h-4 w-4"></lucide-icon>
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="14">
                    <div class="flex flex-col items-center gap-2 py-12 text-text-faint">
                      <lucide-icon [img]="icons.FlaskConical" class="h-8 w-8"></lucide-icon>
                      <span class="text-sm">No backtests yet — upload an MT5 Strategy Tester .xlsx to start.</span>
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
  `,
})
export class BacktestsComponent implements OnInit {
  rows = signal<BacktestSummary[]>([]);
  loading = signal(true);
  uploading = signal(false);
  error = signal('');
  selectedIds = signal<string[]>([]);
  private curves = new Map<string, EquityPoint[]>();
  overlaySeries = signal<ApexAxisChartSeries>([]);
  readonly icons = { FlaskConical, Upload, Trash2, GitCompare };

  constructor(private api: ApiService) {}

  async ngOnInit() { await this.reload(); }

  async reload() {
    this.loading.set(true);
    try {
      this.rows.set(await firstValueFrom(this.api.get<BacktestSummary[]>('/api/backtests')));
    } catch (e: any) {
      this.error.set(e?.error?.error || e?.message || 'โหลดรายการ backtest ไม่สำเร็จ');
    } finally {
      this.loading.set(false);
    }
  }

  isSelected(id: string) { return this.selectedIds().includes(id); }

  async toggle(id: string) {
    const cur = this.selectedIds();
    this.selectedIds.set(cur.includes(id) ? cur.filter(x => x !== id) : [...cur, id]);
    await this.rebuildOverlay();
  }

  async onFile(ev: Event) {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.error.set('');
    this.uploading.set(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      await firstValueFrom(this.api.post('/api/backtests', fd));
      await this.reload();
    } catch (e: any) {
      this.error.set(e?.error?.error || e?.message || 'อัปโหลดไม่สำเร็จ');
    } finally {
      this.uploading.set(false);
      input.value = '';
    }
  }

  async remove(b: BacktestSummary) {
    if (!confirm(`Delete backtest "${b.expertName}"?`)) return;
    await firstValueFrom(this.api.delete(`/api/backtests/${b.id}`));
    this.selectedIds.set(this.selectedIds().filter(x => x !== b.id));
    await this.reload();
    await this.rebuildOverlay();
  }

  // Fetch (and cache) each selected backtest's equity curve, then build one series
  // per backtest as Return % = balance/initialDeposit*100 - 100, x = trade index.
  private async rebuildOverlay() {
    const ids = this.selectedIds();
    const series: ApexAxisChartSeries = [];
    for (const id of ids) {
      if (!this.curves.has(id)) {
        const d = await firstValueFrom(this.api.get<{ equityCurve: EquityPoint[] }>(`/api/backtests/${id}`));
        this.curves.set(id, d.equityCurve);
      }
      const meta = this.rows().find(r => r.id === id);
      const dep = meta?.initialDeposit || 1;
      const pts = this.curves.get(id)!;
      series.push({
        name: meta?.expertName ?? id,
        data: pts.map((p, i) => ({ x: i, y: +(p.balance / dep * 100 - 100).toFixed(2) })),
      });
    }
    this.overlaySeries.set(series);
  }

  readonly chart: ApexChart = { type: 'line', height: 320, background: 'transparent', toolbar: { show: false }, zoom: { enabled: false }, fontFamily: 'inherit', animations: { enabled: true, speed: 400 } };
  readonly stroke: ApexStroke = { curve: 'smooth', width: 2 };
  readonly dataLabels: ApexDataLabels = { enabled: false };
  readonly grid: ApexGrid = { borderColor: 'rgba(255,255,255,0.04)', strokeDashArray: 4, xaxis: { lines: { show: false } } };
  readonly xaxis: ApexXAxis = { type: 'numeric', title: { text: 'Trade #', style: { color: '#8b8f9e' } }, labels: { style: { colors: '#8b8f9e', fontSize: '11px' } } };
  readonly yaxis: ApexYAxis = { labels: { style: { colors: '#8b8f9e', fontSize: '11px' }, formatter: (v: number) => v.toFixed(0) + '%' } };
  readonly tooltip: ApexTooltip = { theme: 'dark', y: { formatter: (v: number) => v.toFixed(2) + '%' } };
  readonly legend: ApexLegend = { labels: { colors: '#c8ccd6' } };
}
