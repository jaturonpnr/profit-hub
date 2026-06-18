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

interface EquityPoint { t: string; balance: number; }
interface HeatCell { dow: number; hour: number; netProfit: number; tradeCount: number; }
interface MonthRow { periodStart: string; netProfit: number; tradeCount: number; }
interface RecentTrade { closeTimeUtc: string; symbol: string; direction: string; lots: number; netProfit: number; commission: number; swap: number; }
interface EaDetail {
  magicNumber: number; name: string; netProfit: number; tradeCount: number; winRate: number;
  profitFactor: number | null; expectancy: number; drawdownAmount: number; drawdownPct: number;
  swap: number; commission: number; firstTradeUtc: string; lastTradeUtc: string;
  equityCurve: EquityPoint[]; heatmap: HeatCell[]; monthly: MonthRow[]; recentTrades: RecentTrade[];
}

const DOW = ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];

/** EA detail — KPIs + equity curve + day×hour heatmap + recent trades. */
@Component({
  selector: 'ph-ea-detail',
  standalone: true,
  imports: [DecimalPipe, DatePipe, RouterLink, NgApexchartsModule, LucideAngularModule, UiCardComponent, UiSpinnerComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <a routerLink="/eas" class="inline-flex items-center gap-1.5 text-sm text-text-muted hover:text-text transition-colors">
        <lucide-icon [img]="icons.ArrowLeft" class="h-4 w-4"></lucide-icon> Back to EAs
      </a>

      @if (loading()) { <ui-spinner label="Loading EA…" /> }
      @else if (d() !== null) { @if (d(); as ea) {
        <header class="flex flex-col gap-1">
          <h1 class="text-xl font-semibold tracking-tight">{{ ea.name || ('EA #' + ea.magicNumber) }}</h1>
          <p class="text-sm text-text-muted">magic #{{ ea.magicNumber }} · {{ ea.firstTradeUtc | date:'yyyy-MM-dd' }} → {{ ea.lastTradeUtc | date:'yyyy-MM-dd' }}</p>
        </header>

        <div class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Net Profit</div><div class="mt-1 text-lg font-semibold tabular-nums" [class.text-profit]="ea.netProfit >= 0" [class.text-loss]="ea.netProfit < 0">{{ ea.netProfit | number:'1.2-2' }}</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Win rate</div><div class="mt-1 text-lg font-semibold tabular-nums">{{ ea.winRate | number:'1.0-1' }}%</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Profit factor</div><div class="mt-1 text-lg font-semibold tabular-nums">{{ ea.profitFactor === null ? '∞' : (ea.profitFactor | number:'1.2-2') }}</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Expectancy</div><div class="mt-1 text-lg font-semibold tabular-nums" [class.text-profit]="ea.expectancy >= 0" [class.text-loss]="ea.expectancy < 0">{{ ea.expectancy | number:'1.2-2' }}</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Realized DD</div><div class="mt-1 text-lg font-semibold tabular-nums text-loss">{{ ea.drawdownAmount | number:'1.0-0' }} ({{ ea.drawdownPct | number:'1.1-1' }}%)</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Trades</div><div class="mt-1 text-lg font-semibold tabular-nums">{{ ea.tradeCount }}</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Swap</div><div class="mt-1 text-lg font-semibold tabular-nums">{{ ea.swap | number:'1.2-2' }}</div></div>
          <div class="rounded-lg border border-border bg-surface p-4"><div class="text-xs text-text-faint">Commission</div><div class="mt-1 text-lg font-semibold tabular-nums">{{ ea.commission | number:'1.2-2' }}</div></div>
        </div>

        <ui-card [hasHeader]="true">
          <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">Cumulative net profit</h2></div>
          <div class="px-2 pb-2 pt-4"><apx-chart [series]="series()" [chart]="chart" [colors]="['#8b5cf6']" [fill]="fill" [stroke]="stroke" [dataLabels]="dataLabels" [grid]="grid" [xaxis]="xaxis" [yaxis]="yaxis" [tooltip]="tooltip"></apx-chart></div>
        </ui-card>

        <ui-card [hasHeader]="true">
          <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">By day &amp; hour (Net Profit)</h2></div>
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

        <ui-card [hasHeader]="true">
          <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">Recent trades</h2></div>
          <div class="overflow-x-auto">
            <table class="w-full text-xs">
              <thead><tr class="text-text-faint"><th class="px-3 py-2 text-left">Close</th><th class="px-3 py-2 text-left">Symbol</th><th class="px-3 py-2 text-left">Dir</th><th class="px-3 py-2 text-right">Lots</th><th class="px-3 py-2 text-right">Net</th></tr></thead>
              <tbody>
                @for (t of ea.recentTrades; track t.closeTimeUtc) {
                  <tr class="border-t border-border-subtle">
                    <td class="px-3 py-1.5 tabular-nums text-text-muted">{{ t.closeTimeUtc | date:'yyyy-MM-dd HH:mm' }}</td>
                    <td class="px-3 py-1.5">{{ t.symbol }}</td>
                    <td class="px-3 py-1.5 uppercase text-text-muted">{{ t.direction }}</td>
                    <td class="px-3 py-1.5 text-right tabular-nums">{{ t.lots | number:'1.2-2' }}</td>
                    <td class="px-3 py-1.5 text-right tabular-nums" [class.text-profit]="t.netProfit >= 0" [class.text-loss]="t.netProfit < 0">{{ t.netProfit | number:'1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </ui-card>
      } }
      @else { <ui-card><div class="py-10 text-center text-sm text-text-faint">EA not found.</div></ui-card> }
    </div>
  `,
})
export class EaDetailComponent implements OnInit {
  d = signal<EaDetail | null>(null);
  loading = signal(true);
  readonly icons = { ArrowLeft };
  readonly dows = DOW;
  readonly hours = Array.from({ length: 24 }, (_, i) => i);

  constructor(private api: ApiService, private route: ActivatedRoute) {}

  async ngOnInit() {
    const magic = this.route.snapshot.paramMap.get('magic')!;
    try {
      this.d.set(await firstValueFrom(this.api.get<EaDetail>(`/api/eas/${magic}`)));
    } catch {
      this.d.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  series = computed<ApexAxisChartSeries>(() => [{
    name: 'Cumulative', data: (this.d()?.equityCurve ?? []).map(p => ({ x: new Date(p.t).getTime(), y: p.balance })),
  }]);

  private maxAbsHeat = computed(() => Math.max(1, ...(this.d()?.heatmap ?? []).map(c => Math.abs(c.netProfit))));
  private heatCell(dow: number, hour: number) { return this.d()?.heatmap.find(c => c.dow === dow && c.hour === hour); }
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

  readonly chart: ApexChart = { type: 'area', height: 280, background: 'transparent', toolbar: { show: false }, zoom: { enabled: false }, fontFamily: 'inherit', animations: { enabled: true, speed: 500 } };
  readonly fill: ApexFill = { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.45, opacityTo: 0, stops: [0, 95], colorStops: [{ offset: 0, color: '#8b5cf6', opacity: 0.45 }, { offset: 100, color: '#8b5cf6', opacity: 0 }] } };
  readonly stroke: ApexStroke = { curve: 'smooth', width: 2 };
  readonly dataLabels: ApexDataLabels = { enabled: false };
  readonly grid: ApexGrid = { borderColor: 'rgba(255,255,255,0.04)', strokeDashArray: 4, xaxis: { lines: { show: false } } };
  readonly xaxis: ApexXAxis = { type: 'datetime', labels: { style: { colors: '#8b8f9e', fontSize: '11px' } } };
  readonly yaxis: ApexYAxis = { labels: { style: { colors: '#8b8f9e', fontSize: '11px' }, formatter: (v: number) => v.toFixed(0) } };
  readonly tooltip: ApexTooltip = { theme: 'dark', x: { format: 'yyyy-MM-dd HH:mm' }, y: { formatter: (v: number) => v.toFixed(2) } };
}
