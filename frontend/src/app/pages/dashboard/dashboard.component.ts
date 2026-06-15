import { Component, OnInit, computed, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import {
  NgApexchartsModule, ApexChart, ApexAxisChartSeries, ApexFill, ApexStroke,
  ApexDataLabels, ApexGrid, ApexXAxis, ApexYAxis, ApexTooltip, ApexMarkers,
} from 'ng-apexcharts';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';
import {
  UiStatCardComponent, UiCardComponent, UiTableComponent, UiBadgeComponent, UiSpinnerComponent,
} from '../../shared/ui';

interface SummaryRow { periodStart: string; netProfit: number; tradeCount: number; wins: number; }
interface AccountRow { accountId: string; name: string; accountNumber: number; netProfit: number; tradeCount: number; }

/**
 * Dashboard — 4 stat cards (Today / Week / Month / All-time) with count-up +
 * ApexCharts sparklines, a violet gradient cumulative-P/L area chart, and dense
 * Daily P/L + By-EA tables.
 *
 * Presentation only. All data logic is preserved verbatim: reload() and its
 * Promise.all fetches, currentPeriodKeys(), the Bangkok-tz card lookups, the
 * today/week/month/allTime signals, the days()/byAccount() signals, and the
 * <ph-filter-bar (changed)="reload()"> wiring. The chart data prep (reverse to
 * ascending + cumulative sum) is reused; only the render target changed to an
 * ApexCharts area chart with series bound via signals.
 */
@Component({
  selector: 'ph-dashboard',
  standalone: true,
  imports: [
    FilterBarComponent, DecimalPipe, NgApexchartsModule,
    UiStatCardComponent, UiCardComponent, UiTableComponent, UiBadgeComponent, UiSpinnerComponent,
  ],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <div>
        <h1 class="text-xl font-semibold tracking-tight">Dashboard</h1>
        <p class="text-sm text-text-muted mt-0.5">Realised P/L across your accounts.</p>
      </div>

      <ph-filter-bar (changed)="reload()" />

      @if (loading()) {
        <ui-spinner label="Loading dashboard…" />
      } @else {
      <!-- Stat cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <ui-stat-card label="Today" [value]="today()" [series]="dailySpark()" [secondary]="thb(today())" />
        <ui-stat-card label="This week" [value]="week()" [series]="dailySpark()" [secondary]="thb(week())" />
        <ui-stat-card label="This month" [value]="month()" [series]="dailySpark()" [secondary]="thb(month())" />
        <ui-stat-card label="All time" [value]="allTime()" [series]="cumulativeSpark()" [secondary]="thb(allTime())" />
      </div>

      <!-- Cumulative P/L area chart -->
      <ui-card [hasHeader]="true" [padded]="false">
        <div uiCardHeader>
          <h2 class="text-sm font-medium text-text-muted">Cumulative net profit</h2>
        </div>
        <div class="px-2 pb-2 pt-4">
          <apx-chart
            [series]="cumulativeSeries()"
            [chart]="chart"
            [colors]="['#8b5cf6']"
            [fill]="fill"
            [stroke]="stroke"
            [dataLabels]="dataLabels"
            [grid]="grid"
            [xaxis]="xaxis()"
            [yaxis]="yaxis"
            [tooltip]="tooltip"
            [markers]="markers"
          ></apx-chart>
        </div>
      </ui-card>

      <!-- Tables -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        <div class="flex flex-col gap-3">
          <h2 class="text-sm font-medium text-text-muted">Daily P/L</h2>
          <ui-table dense>
            <table>
              <thead>
                <tr>
                  <th>Day</th>
                  <th class="!text-right">Net</th>
                  <th class="!text-right">Trades</th>
                  <th>Win %</th>
                </tr>
              </thead>
              <tbody>
                @for (r of days(); track r.periodStart) {
                  <tr>
                    <td class="tabular-nums text-text-muted">{{ r.periodStart }}</td>
                    <td
                      class="text-right tabular-nums font-medium"
                      [class.text-profit]="r.netProfit >= 0"
                      [class.text-loss]="r.netProfit < 0"
                    >{{ r.netProfit | number:'1.2-2' }}</td>
                    <td class="text-right tabular-nums text-text-muted">{{ r.tradeCount }}</td>
                    <td>
                      <div class="flex items-center gap-2">
                        <div class="h-1.5 w-16 rounded-full bg-surface-raised overflow-hidden">
                          <div
                            class="h-full rounded-full bg-brand-500"
                            [style.width.%]="r.tradeCount ? (100 * r.wins / r.tradeCount) : 0"
                          ></div>
                        </div>
                        <span class="text-xs tabular-nums text-text-muted">{{ r.tradeCount ? (100 * r.wins / r.tradeCount).toFixed(0) : 0 }}%</span>
                      </div>
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="!py-8 text-center text-sm text-text-faint">No data for this range.</td></tr>
                }
              </tbody>
            </table>
          </ui-table>
        </div>

        <div class="flex flex-col gap-3">
          <h2 class="text-sm font-medium text-text-muted">By Account</h2>
          <ui-table dense>
            <table>
              <thead>
                <tr>
                  <th>Account</th>
                  <th class="!text-right">Net</th>
                  <th class="!text-right">Trades</th>
                </tr>
              </thead>
              <tbody>
                @for (r of byAccount(); track r.accountId) {
                  <tr>
                    <td>
                      <ui-badge variant="brand">{{ r.name }}</ui-badge>
                    </td>
                    <td
                      class="text-right tabular-nums font-medium"
                      [class.text-profit]="r.netProfit >= 0"
                      [class.text-loss]="r.netProfit < 0"
                    >{{ r.netProfit | number:'1.2-2' }}</td>
                    <td class="text-right tabular-nums text-text-muted">{{ r.tradeCount }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="3" class="!py-8 text-center text-sm text-text-faint">No EA activity for this range.</td></tr>
                }
              </tbody>
            </table>
          </ui-table>
        </div>
      </div>
      }
    </div>
  `,
})
export class DashboardComponent implements OnInit {
  days = signal<SummaryRow[]>([]);
  byAccount = signal<AccountRow[]>([]);
  today = signal(0); week = signal(0); month = signal(0); allTime = signal(0);
  fxRate = signal<number | null>(null); // USD→THB; null = hide THB line
  loading = signal(true); // spinner until the first load resolves

  constructor(private api: ApiService, private filter: FilterService, private auth: AuthService) {}
  async ngOnInit() {
    // FX rate is global and independent of the trade filters — fetch once.
    firstValueFrom(this.api.get<{ rate: number | null }>('/api/fx'))
      .then(fx => this.fxRate.set(fx.rate)).catch(() => this.fxRate.set(null));
    await this.reload();
  }

  /** Format a USD amount as an approximate THB line, or '' when no rate is available. */
  thb(usd: number): string {
    const r = this.fxRate();
    if (r == null) return '';
    return '≈ ฿' + (usd * r).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  /**
   * Ascending daily rows (reverse of the descending API order). Shared by the
   * cumulative chart and the sparklines — this is the existing draw() data prep.
   */
  private ascDays = computed(() => [...this.days()].reverse());

  /** Cumulative net-profit series (ascending). Reuses the old draw() cumulative sum. */
  private cumulative = computed(() => {
    let cum = 0;
    return this.ascDays().map(d => (cum += d.netProfit));
  });

  /** Main area-chart series: x = period labels, y = cumulative net profit. */
  cumulativeSeries = computed<ApexAxisChartSeries>(() => [{
    name: 'Cumulative Net Profit',
    data: this.cumulative(),
  }]);

  /** X-axis category labels for the area chart. */
  xaxis = computed<ApexXAxis>(() => ({
    categories: this.ascDays().map(d => d.periodStart),
    labels: { style: { colors: '#8b8f9e', fontSize: '11px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
    tooltip: { enabled: false },
  }));

  /** Sparkline series — last ~12 daily netProfit values (derived, no fetch). */
  dailySpark = computed(() => this.ascDays().slice(-12).map(d => d.netProfit));

  /** Sparkline series — last ~12 cumulative values (derived, no fetch). */
  cumulativeSpark = computed(() => this.cumulative().slice(-12));

  // ── ApexCharts static options ──────────────────────────────────────────────
  readonly chart: ApexChart = {
    type: 'area',
    height: 280,
    background: 'transparent',
    toolbar: { show: false },
    zoom: { enabled: false },
    fontFamily: 'inherit',
    animations: { enabled: true, speed: 500 },
  };

  readonly fill: ApexFill = {
    type: 'gradient',
    gradient: {
      shadeIntensity: 1,
      opacityFrom: 0.45,
      opacityTo: 0,
      stops: [0, 95],
      colorStops: [
        { offset: 0, color: '#8b5cf6', opacity: 0.45 },
        { offset: 100, color: '#8b5cf6', opacity: 0 },
      ],
    },
  };

  readonly stroke: ApexStroke = { curve: 'smooth', width: 2 };
  readonly dataLabels: ApexDataLabels = { enabled: false };
  readonly markers: ApexMarkers = { size: 0, hover: { size: 4 } };

  readonly grid: ApexGrid = {
    borderColor: 'rgba(255,255,255,0.04)',
    strokeDashArray: 4,
    xaxis: { lines: { show: false } },
    yaxis: { lines: { show: true } },
    padding: { left: 8, right: 8 },
  };

  readonly yaxis: ApexYAxis = {
    labels: {
      style: { colors: '#8b8f9e', fontSize: '11px' },
      formatter: (v: number) => v.toFixed(0),
    },
  };

  readonly tooltip: ApexTooltip = {
    theme: 'dark',
    x: { show: true },
    y: { formatter: (v: number) => v.toFixed(2) },
  };

  /**
   * Compute the periodStart keys ("yyyy-MM-dd") for the current week and month in
   * the user's reporting timezone, matching how the backend buckets weekly/monthly
   * summary rows. Weeks bucket to the ISO Monday of the week; months bucket to the 1st.
   */
  private currentPeriodKeys(): { weekStr: string; monthStr: string } {
    // en-CA / sv-SE both yield yyyy-MM-dd; parse tz-local today into y/m/d.
    const [y, m, d] = new Date()
      .toLocaleDateString('en-CA', { timeZone: this.auth.timeZone })
      .split('-')
      .map(Number);
    // Anchor at UTC midnight so weekday/date arithmetic is timezone-agnostic.
    const base = new Date(Date.UTC(y, m - 1, d));
    // getUTCDay: 0=Sun..6=Sat. Convert to ISO Monday-start offset (Mon=0..Sun=6).
    const isoOffset = (base.getUTCDay() + 6) % 7;
    const monday = new Date(base);
    monday.setUTCDate(base.getUTCDate() - isoOffset);
    const fmt = (dt: Date) => dt.toISOString().slice(0, 10);
    const monthStr = `${String(y).padStart(4, '0')}-${String(m).padStart(2, '0')}-01`;
    return { weekStr: fmt(monday), monthStr };
  }

  async reload() {
    const p = this.filter.queryParams();
    const [days, weeks, months, byAccount] = await Promise.all([
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'day' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'week' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'month' })),
      firstValueFrom(this.api.get<AccountRow[]>('/api/summary/by-account', p)),
    ]);
    this.days.set(days); this.byAccount.set(byAccount);
    // The backend buckets summary rows in the user's configured timezone (the `tz`
    // JWT claim). We derive "today" in that same timezone so the lookup matches those
    // local-date periodStart values.
    const todayStr = new Date().toLocaleDateString('sv-SE', { timeZone: this.auth.timeZone });
    const { weekStr, monthStr } = this.currentPeriodKeys();
    this.today.set(days.find(d => d.periodStart === todayStr)?.netProfit ?? 0);
    this.week.set(weeks.find(w => w.periodStart === weekStr)?.netProfit ?? 0);
    this.month.set(months.find(m => m.periodStart === monthStr)?.netProfit ?? 0);
    this.allTime.set(days.reduce((s, d) => s + d.netProfit, 0));
    this.loading.set(false);
  }
}
