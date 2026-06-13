import { AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { Chart, registerables } from 'chart.js';
import { ApiService } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';

Chart.register(...registerables);

interface SummaryRow { periodStart: string; netProfit: number; tradeCount: number; wins: number; }
interface EaRow { magicNumber: number; name: string; netProfit: number; tradeCount: number; }

@Component({
  selector: 'ph-dashboard',
  standalone: true,
  imports: [FilterBarComponent, DecimalPipe],
  template: `
    <h1>Dashboard</h1>
    <ph-filter-bar (changed)="reload()" />
    <div class="cards">
      <div class="card"><span>Today</span><b [class.neg]="today() < 0">{{ today() | number:'1.2-2' }}</b></div>
      <div class="card"><span>This week</span><b [class.neg]="week() < 0">{{ week() | number:'1.2-2' }}</b></div>
      <div class="card"><span>This month</span><b [class.neg]="month() < 0">{{ month() | number:'1.2-2' }}</b></div>
      <div class="card"><span>All time</span><b [class.neg]="allTime() < 0">{{ allTime() | number:'1.2-2' }}</b></div>
    </div>
    <canvas #chart height="80"></canvas>
    <div class="tables">
      <table>
        <caption>Daily P/L</caption>
        <tr><th>Day</th><th>Net</th><th>Trades</th><th>Win %</th></tr>
        @for (r of days(); track r.periodStart) {
          <tr><td>{{ r.periodStart }}</td>
              <td [class.neg]="r.netProfit < 0">{{ r.netProfit | number:'1.2-2' }}</td>
              <td>{{ r.tradeCount }}</td>
              <td>{{ r.tradeCount ? (100 * r.wins / r.tradeCount).toFixed(0) : 0 }}%</td></tr>
        }
      </table>
      <table>
        <caption>By EA</caption>
        <tr><th>EA</th><th>Net</th><th>Trades</th></tr>
        @for (r of eas(); track r.magicNumber) {
          <tr><td>{{ r.name }}</td>
              <td [class.neg]="r.netProfit < 0">{{ r.netProfit | number:'1.2-2' }}</td>
              <td>{{ r.tradeCount }}</td></tr>
        }
      </table>
    </div>
  `,
  styles: [`.cards{display:flex;gap:1rem;margin:1rem 0}
            .card{padding:1rem;border:1px solid #2a2f3a;border-radius:8px;min-width:140px}
            .card b{display:block;font-size:1.4rem;color:#30a46c}
            b.neg,td.neg{color:#e5484d!important}
            .tables{display:flex;gap:2rem;margin-top:1.5rem;align-items:flex-start}`],
})
export class DashboardComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('chart') chartRef!: ElementRef<HTMLCanvasElement>;
  days = signal<SummaryRow[]>([]);
  eas = signal<EaRow[]>([]);
  today = signal(0); week = signal(0); month = signal(0); allTime = signal(0);
  private chart?: Chart;

  constructor(private api: ApiService, private filter: FilterService) {}
  async ngOnInit() { await this.reload(); }
  ngAfterViewInit() { this.draw(); }
  ngOnDestroy() { this.chart?.destroy(); }

  /**
   * Compute the periodStart keys ("yyyy-MM-dd") for the current week and month in
   * Asia/Bangkok, matching how the backend buckets weekly/monthly summary rows.
   * Weeks bucket to the ISO Monday of the week; months bucket to the 1st.
   * NOTE: timezone is hardcoded to Asia/Bangkok pending the authenticated user's
   * own timezone claim — only correct while that timezone is Asia/Bangkok.
   */
  private currentPeriodKeys(): { weekStr: string; monthStr: string } {
    // en-CA / sv-SE both yield yyyy-MM-dd; parse Bangkok-local today into y/m/d.
    const [y, m, d] = new Date()
      .toLocaleDateString('en-CA', { timeZone: 'Asia/Bangkok' })
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
    const [days, weeks, months, eas] = await Promise.all([
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'day' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'week' })),
      firstValueFrom(this.api.get<SummaryRow[]>('/api/summary', { ...p, period: 'month' })),
      firstValueFrom(this.api.get<EaRow[]>('/api/summary/by-ea', p)),
    ]);
    this.days.set(days); this.eas.set(eas);
    // The backend buckets summary rows in the user's configured timezone (defaults to
    // Asia/Bangkok). We hardcode Asia/Bangkok here to derive "today" so the lookup matches
    // those local-date periodStart values. TODO: this should ideally use the authenticated
    // user's own timezone — it is only correct while that timezone is Asia/Bangkok.
    const todayStr = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Bangkok' });
    const { weekStr, monthStr } = this.currentPeriodKeys();
    this.today.set(days.find(d => d.periodStart === todayStr)?.netProfit ?? 0);
    this.week.set(weeks.find(w => w.periodStart === weekStr)?.netProfit ?? 0);
    this.month.set(months.find(m => m.periodStart === monthStr)?.netProfit ?? 0);
    this.allTime.set(days.reduce((s, d) => s + d.netProfit, 0));
    this.draw();
  }

  private draw() {
    if (!this.chartRef) return;
    const asc = [...this.days()].reverse();
    let cum = 0;
    const data = asc.map(d => (cum += d.netProfit));
    this.chart?.destroy();
    this.chart = new Chart(this.chartRef.nativeElement, {
      type: 'line',
      data: { labels: asc.map(d => d.periodStart),
              datasets: [{ label: 'Cumulative Net Profit', data, borderColor: '#30a46c', tension: 0.2, pointRadius: 0 }] },
      options: { plugins: { legend: { display: false } } },
    });
  }
}
