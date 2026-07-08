import { Component, OnInit, signal, computed } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  NgApexchartsModule, ApexChart, ApexAxisChartSeries, ApexFill, ApexStroke,
  ApexDataLabels, ApexGrid, ApexXAxis, ApexYAxis, ApexTooltip,
} from 'ng-apexcharts';
import { LucideAngularModule, ArrowLeft } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiSpinnerComponent } from '../../shared/ui';
import { BacktestSummary } from './backtests.component';

interface EquityPoint { t: string; balance: number; }
interface InputEntry { section: string; key: string; value: string; }
interface HeatCell { dow: number; hour: number; netProfit: number; tradeCount: number; }
interface MonthlyRow { month: string; netProfit: number; tradeCount: number; }

const DOW = ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];

/** Trade-stat tiles in display order; keys match backend tradeStats map. */
const STAT_TILES: { key: string; label: string; loss?: boolean }[] = [
  { key: 'largestWin', label: 'ไม้กำไรใหญ่สุด' },
  { key: 'largestLoss', label: 'ไม้ขาดทุนใหญ่สุด', loss: true },
  { key: 'avgWin', label: 'กำไรเฉลี่ย/ไม้' },
  { key: 'avgLoss', label: 'ขาดทุนเฉลี่ย/ไม้', loss: true },
  { key: 'maxConsecWins', label: 'ชนะติดกันสูงสุด' },
  { key: 'maxConsecLosses', label: 'แพ้ติดกันสูงสุด' },
  { key: 'avgHolding', label: 'เวลาถือเฉลี่ย' },
  { key: 'maxHolding', label: 'ถือนานสุด' },
];

/** Backtest detail — full KPIs + trade stats + equity curve + heatmap + monthly. */
@Component({
  selector: 'ph-backtest-detail',
  standalone: true,
  imports: [DecimalPipe, DatePipe, RouterLink, NgApexchartsModule, LucideAngularModule, UiCardComponent, UiSpinnerComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <a routerLink="/backtests" class="inline-flex items-center gap-1.5 text-sm text-text-muted hover:text-text transition-colors">
        <lucide-icon [img]="icons.ArrowLeft" class="h-4 w-4"></lucide-icon> Back to backtests
      </a>

      @if (loading()) {
        <ui-spinner label="Loading backtest…" />
      } @else if (s() !== null) {
        @if (s(); as b) {
        <header class="flex flex-col gap-1">
          <h1 class="text-xl font-semibold tracking-tight">{{ b.expertName }}</h1>
          <p class="text-sm text-text-muted">
            {{ b.symbol }} · {{ b.timeframe }} ·
            {{ b.periodFrom | date:'yyyy-MM-dd' }} → {{ b.periodTo | date:'yyyy-MM-dd' }}
            @if (b.magicNumber) { · magic #{{ b.magicNumber }} }
          </p>
        </header>

        <div class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Net Profit</div>
            <div class="mt-1 text-lg font-semibold tabular-nums" [class.text-profit]="b.netProfit >= 0" [class.text-loss]="b.netProfit < 0">{{ b.netProfit | number:'1.2-2' }} {{ b.currency }}</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Return % (on {{ b.initialDeposit | number:'1.0-0' }})</div>
            <div class="mt-1 text-lg font-semibold tabular-nums" [class.text-profit]="b.returnPct >= 0" [class.text-loss]="b.returnPct < 0">{{ b.returnPct | number:'1.1-1' }}%</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Max Equity DD</div>
            <div class="mt-1 text-lg font-semibold tabular-nums text-loss">{{ b.equityDrawdownMaxPct | number:'1.2-2' }}%</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Profit Factor</div>
            <div class="mt-1 text-lg font-semibold tabular-nums">{{ b.profitFactor | number:'1.2-2' }}</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Recovery Factor</div>
            <div class="mt-1 text-lg font-semibold tabular-nums">{{ b.recoveryFactor | number:'1.2-2' }}</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Sharpe Ratio</div>
            <div class="mt-1 text-lg font-semibold tabular-nums">{{ b.sharpeRatio | number:'1.2-2' }}</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Trades</div>
            <div class="mt-1 text-lg font-semibold tabular-nums">{{ b.totalTrades }}</div>
          </div>
          <div class="rounded-lg border border-border bg-surface p-4">
            <div class="text-xs text-text-faint">Win %</div>
            <div class="mt-1 text-lg font-semibold tabular-nums">{{ b.winRatePct | number:'1.1-1' }}%</div>
          </div>
        </div>

        @if (statTiles().length > 0) {
          <ui-card [hasHeader]="true">
            <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">สถิติรายไม้</h2></div>
            <div class="grid grid-cols-2 gap-4 px-4 pb-4 pt-3 sm:grid-cols-3 lg:grid-cols-4">
              @for (t of statTiles(); track t.key) {
                <div>
                  <div class="text-xs text-text-faint">{{ t.label }}</div>
                  <div class="mt-1 text-sm font-semibold tabular-nums" [class.text-loss]="t.loss">{{ t.value }}</div>
                </div>
              }
            </div>
          </ui-card>
        }

        <ui-card [hasHeader]="true">
          <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">Equity curve (balance over time)</h2></div>
          <div class="px-2 pb-2 pt-4">
            <apx-chart [series]="series()" [chart]="chart" [colors]="['#8b5cf6']" [fill]="fill" [stroke]="stroke" [dataLabels]="dataLabels" [grid]="grid" [xaxis]="xaxis" [yaxis]="yaxis" [tooltip]="tooltip"></apx-chart>
          </div>
        </ui-card>

        @if (heatmap().length > 0) {
          <ui-card [hasHeader]="true">
            <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">By day &amp; hour (broker time)</h2></div>
            <div class="overflow-x-auto px-2 pb-3 pt-3">
              <table class="border-separate border-spacing-0.5 text-[10px]">
                <thead><tr><th class="px-1 text-text-faint"></th>@for (h of hours; track h) { <th class="px-0.5 text-center font-normal text-text-faint">{{ h }}</th> }</tr></thead>
                <tbody>
                  @for (dw of dows; track dw; let i = $index) {
                    <tr>
                      <td class="pr-1 text-right font-medium text-text-faint">{{ dw }}</td>
                      @for (h of hours; track h) {
                        <td class="h-5 w-5 rounded-sm" [style.background]="heatBg(i, h)" [title]="heatTitle(i, h)"></td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </ui-card>
        } @else if (statTiles().length > 0) {
          <ui-card>
            <div class="px-4 py-3 text-sm text-text-faint">อัปโหลดไฟล์นี้ใหม่อีกครั้งเพื่อดู heatmap และกำไรรายเดือนรายไม้</div>
          </ui-card>
        }

        @if (monthly().length > 0) {
          <ui-card [hasHeader]="true">
            <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">กำไรรายเดือน (backtest)</h2></div>
            <div class="overflow-x-auto">
              <table class="w-full text-xs">
                <thead><tr class="text-text-faint"><th class="px-3 py-2 text-left">เดือน</th><th class="px-3 py-2 text-right">Net Profit</th><th class="px-3 py-2 text-right">ไม้</th></tr></thead>
                <tbody>
                  @for (m of monthly(); track m.month) {
                    <tr class="border-t border-border-subtle">
                      <td class="px-3 py-1.5 tabular-nums text-text-muted">{{ m.month }}</td>
                      <td class="px-3 py-1.5 text-right tabular-nums" [class.text-profit]="m.netProfit >= 0" [class.text-loss]="m.netProfit < 0">{{ m.netProfit | number:'1.2-2' }}</td>
                      <td class="px-3 py-1.5 text-right tabular-nums">{{ m.tradeCount }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </ui-card>
        }

        @if (inputs().length > 0) {
          <ui-card [hasHeader]="true">
            <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">EA Inputs</h2></div>
            <div class="flex flex-col gap-4 px-4 pb-4 pt-3">
              @for (sec of sections(); track sec[0]) {
                <div>
                  <div class="mb-1 text-xs uppercase tracking-wide text-text-faint">{{ sec[0] }}</div>
                  <table class="w-full text-sm">
                    <tbody>
                      @for (i of sec[1]; track i.key) {
                        <tr class="border-b border-border/40 last:border-0">
                          <td class="py-1 pr-4 font-mono text-xs text-text-muted">{{ i.key }}</td>
                          <td class="py-1 text-right tabular-nums">{{ i.value }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          </ui-card>
        }
        }
      }
    </div>
  `,
})
export class BacktestDetailComponent implements OnInit {
  s = signal<BacktestSummary | null>(null);
  loading = signal(true);
  private points = signal<EquityPoint[]>([]);
  inputs = signal<InputEntry[]>([]);
  tradeStats = signal<Record<string, string>>({});
  heatmap = signal<HeatCell[]>([]);
  monthly = signal<MonthlyRow[]>([]);
  readonly icons = { ArrowLeft };
  readonly dows = DOW;
  readonly hours = Array.from({ length: 24 }, (_, i) => i);

  // Tiles in fixed order, skipping keys absent from the report.
  statTiles = computed(() => {
    const stats = this.tradeStats();
    return STAT_TILES.filter(t => stats[t.key] !== undefined && stats[t.key] !== '')
      .map(t => ({ ...t, value: stats[t.key] }));
  });

  // Inputs grouped by section, preserving report order ("" section → "Settings").
  sections = computed(() => {
    const m = new Map<string, InputEntry[]>();
    for (const i of this.inputs()) {
      const k = i.section || 'Settings';
      if (!m.has(k)) m.set(k, []);
      m.get(k)!.push(i);
    }
    return [...m.entries()];
  });

  constructor(private api: ApiService, private route: ActivatedRoute) {}

  async ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id')!;
    try {
      const d = await firstValueFrom(this.api.get<{
        summary: BacktestSummary; equityCurve: EquityPoint[]; inputs: InputEntry[];
        tradeStats: Record<string, string>; heatmap: HeatCell[]; monthly: MonthlyRow[];
      }>(`/api/backtests/${id}`));
      this.s.set(d.summary);
      this.points.set(d.equityCurve);
      this.inputs.set(d.inputs ?? []);
      this.tradeStats.set(d.tradeStats ?? {});
      this.heatmap.set(d.heatmap ?? []);
      this.monthly.set(d.monthly ?? []);
    } finally {
      this.loading.set(false);
    }
  }

  private maxAbsHeat = computed(() => Math.max(1, ...this.heatmap().map(c => Math.abs(c.netProfit))));
  // O(1) lookup keyed by "dow-hour", rebuilt only when the backtest changes (vs .find() per cell per CD cycle).
  private heatMap = computed(() => new Map(this.heatmap().map(c => [`${c.dow}-${c.hour}`, c])));
  private heatCell(dow: number, hour: number) { return this.heatMap().get(`${dow}-${hour}`); }
  heatBg(dow: number, hour: number): string {
    const c = this.heatCell(dow, hour);
    if (!c) return 'rgba(255,255,255,0.03)';
    const a = Math.min(0.85, 0.15 + 0.7 * Math.abs(c.netProfit) / this.maxAbsHeat());
    return c.netProfit >= 0 ? `rgba(48,164,108,${a})` : `rgba(229,72,77,${a})`;
  }
  heatTitle(dow: number, hour: number): string {
    const c = this.heatCell(dow, hour);
    return c ? `${DOW[dow]} ${hour}:00 — ${c.netProfit.toFixed(2)} (${c.tradeCount})` : '';
  }

  series = computed<ApexAxisChartSeries>(() => [{
    name: 'Balance',
    data: this.points().map(p => ({ x: new Date(p.t).getTime(), y: p.balance })),
  }]);

  readonly chart: ApexChart = { type: 'area', height: 320, background: 'transparent', toolbar: { show: false }, zoom: { enabled: false }, fontFamily: 'inherit', animations: { enabled: true, speed: 500 } };
  readonly fill: ApexFill = { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.45, opacityTo: 0, stops: [0, 95], colorStops: [{ offset: 0, color: '#8b5cf6', opacity: 0.45 }, { offset: 100, color: '#8b5cf6', opacity: 0 }] } };
  readonly stroke: ApexStroke = { curve: 'smooth', width: 2 };
  readonly dataLabels: ApexDataLabels = { enabled: false };
  readonly grid: ApexGrid = { borderColor: 'rgba(255,255,255,0.04)', strokeDashArray: 4, xaxis: { lines: { show: false } } };
  readonly xaxis: ApexXAxis = { type: 'datetime', labels: { style: { colors: '#8b8f9e', fontSize: '11px' } } };
  readonly yaxis: ApexYAxis = { labels: { style: { colors: '#8b8f9e', fontSize: '11px' }, formatter: (v: number) => v.toFixed(0) } };
  readonly tooltip: ApexTooltip = { theme: 'dark', x: { format: 'yyyy-MM-dd HH:mm' }, y: { formatter: (v: number) => v.toFixed(2) } };
}
