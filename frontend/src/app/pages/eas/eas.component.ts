import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { NgApexchartsModule, ApexChart, ApexAxisChartSeries, ApexStroke, ApexFill } from 'ng-apexcharts';
import { LucideAngularModule, Bot, Check, ArrowUpRight } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiBadgeComponent, UiSpinnerComponent } from '../../shared/ui';

interface Ea {
  magicNumber: number;
  name: string;
  accountName: string;
  netProfit: number;
  tradeCount: number;
  winRate: number;
  profitFactor: number | null;
  expectancy: number;
  drawdownAmount: number;
  drawdownPct: number;
  swap: number;
  commission: number;
  firstTradeUtc: string;
  lastTradeUtc: string;
  sparkline: number[];
  sparkSeries?: ApexAxisChartSeries; // precomputed once so ApexCharts isn't re-fed every CD cycle
  avgExecutionMs: number | null;
}

/** EAs page — one metric card per magic number, with sparkline + headline metrics.
 *  Inline rename preserved. Card links to the EA detail page. */
@Component({
  selector: 'ph-eas',
  standalone: true,
  imports: [FormsModule, DecimalPipe, RouterLink, NgApexchartsModule, LucideAngularModule, UiCardComponent, UiBadgeComponent, UiSpinnerComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <header class="flex flex-col gap-1">
        <h1 class="text-xl font-semibold tracking-tight">EAs</h1>
        <p class="text-sm text-text-muted">Per-EA performance. Click an EA to drill in. One card per magic number.</p>
      </header>

      @if (loading()) {
        <ui-spinner label="Loading EAs…" />
      } @else if (rows().length === 0) {
        <ui-card><div class="flex flex-col items-center gap-2 py-12 text-text-faint">
          <lucide-icon [img]="icons.Bot" class="h-8 w-8"></lucide-icon>
          <span class="text-sm">No EAs yet — they appear once trades are ingested.</span>
        </div></ui-card>
      } @else {
        <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          @for (r of rows(); track r.magicNumber) {
            <ui-card>
              <div class="flex flex-col gap-3">
                <div class="flex items-start justify-between gap-2">
                  <div class="flex flex-col gap-1 min-w-0">
                    <input [(ngModel)]="r.name" (blur)="save(r)" (keyup.enter)="save(r)" [placeholder]="'#' + r.magicNumber"
                      class="h-8 w-full rounded-md border border-border bg-surface-raised px-2.5 text-sm font-medium text-text placeholder:text-text-faint
                             focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
                    <span class="text-[11px] tabular-nums text-text-faint">magic #{{ r.magicNumber }} · {{ r.accountName }}</span>
                  </div>
                  <a [routerLink]="['/eas', r.magicNumber]" class="shrink-0 text-text-faint hover:text-brand-300 transition-colors" title="Drill in" aria-label="Open EA detail">
                    <lucide-icon [img]="icons.ArrowUpRight" class="h-5 w-5"></lucide-icon>
                  </a>
                </div>

                <div class="flex items-end justify-between gap-2">
                  <div>
                    <div class="text-[11px] text-text-faint">Net Profit</div>
                    <div class="text-xl font-semibold tabular-nums" [class.text-profit]="r.netProfit >= 0" [class.text-loss]="r.netProfit < 0">{{ r.netProfit | number:'1.2-2' }}</div>
                    @if (thb(r.netProfit)) {
                      <div class="text-[11px] tabular-nums text-text-faint">{{ thb(r.netProfit) }}</div>
                    }
                  </div>
                  <div class="h-12 w-28">
                    <apx-chart [series]="r.sparkSeries!" [chart]="sparkChart" [stroke]="sparkStroke" [fill]="sparkFill"
                      [colors]="[r.netProfit >= 0 ? '#30a46c' : '#e5484d']"></apx-chart>
                  </div>
                </div>

                <div class="grid grid-cols-2 gap-2 text-xs">
                  <div class="flex justify-between"><span class="text-text-faint">Win rate</span><span class="tabular-nums">{{ r.winRate | number:'1.0-1' }}%</span></div>
                  <div class="flex justify-between"><span class="text-text-faint">Profit factor</span><span class="tabular-nums">{{ r.profitFactor === null ? '∞' : (r.profitFactor | number:'1.2-2') }}</span></div>
                  <div class="flex justify-between"><span class="text-text-faint">Expectancy</span><span class="tabular-nums" [class.text-profit]="r.expectancy >= 0" [class.text-loss]="r.expectancy < 0">{{ r.expectancy | number:'1.2-2' }}</span></div>
                  <div class="flex justify-between"><span class="text-text-faint">Max DD</span><span class="tabular-nums text-loss">{{ r.drawdownAmount | number:'1.0-0' }} ({{ r.drawdownPct | number:'1.0-1' }}%)</span></div>
                  <div class="flex justify-between"><span class="text-text-faint">Trades</span><span class="tabular-nums">{{ r.tradeCount }}</span></div>
                  <div class="flex justify-between"><span class="text-text-faint">Swap+Comm</span><span class="tabular-nums">{{ (r.swap + r.commission) | number:'1.2-2' }}</span></div>
                  <div class="flex justify-between"><span class="text-text-faint" title="Server fill time, approximate — not the journal's 'done in X ms'">Avg fill ≈</span><span class="tabular-nums">{{ r.avgExecutionMs != null ? (r.avgExecutionMs | number:'1.0-0') + ' ms' : '—' }}</span></div>
                </div>
              </div>
            </ui-card>
          }
        </div>
      }
    </div>

    @if (saved()) {
      <div class="fixed bottom-5 left-1/2 z-50 -translate-x-1/2">
        <ui-badge variant="brand"><lucide-icon [img]="icons.Check" class="mr-1 inline h-3.5 w-3.5"></lucide-icon>Saved</ui-badge>
      </div>
    }
  `,
})
export class EasComponent implements OnInit {
  rows = signal<Ea[]>([]);
  saved = signal(false);
  loading = signal(true);
  fxRate = signal<number | null>(null); // USD→THB; null = hide THB line
  readonly icons = { Bot, Check, ArrowUpRight };

  constructor(private api: ApiService) {}

  async ngOnInit() {
    // FX rate is global and independent of trade filters — fetch once.
    firstValueFrom(this.api.get<{ rate: number | null }>('/api/fx'))
      .then(fx => this.fxRate.set(fx.rate)).catch(() => this.fxRate.set(null));
    try {
      const data = await firstValueFrom(this.api.get<Ea[]>('/api/eas'));
      for (const r of data) {
        r.sparkSeries = [{ name: 'Cumulative', data: r.sparkline }];
        this.lastNames.set(r.magicNumber, r.name);
      }
      this.rows.set(data);
    } finally {
      this.loading.set(false);
    }
  }

  private lastNames = new Map<number, string>(); // last successfully-saved name, for rollback

  async save(r: Ea) {
    const trimmed = r.name.trim();
    if (trimmed === this.lastNames.get(r.magicNumber)) return; // no change
    try {
      await firstValueFrom(this.api.put(`/api/ea-names/${r.magicNumber}`, { name: trimmed }));
      this.lastNames.set(r.magicNumber, trimmed);
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 1500);
    } catch {
      r.name = this.lastNames.get(r.magicNumber) ?? ''; // roll back the optimistic ngModel edit
    }
  }

  /** Format a USD amount as an approximate THB line, or '' when no rate is available. */
  thb(usd: number): string {
    const r = this.fxRate();
    if (r == null) return '';
    return '≈ ฿' + (usd * r).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  readonly sparkChart: ApexChart = { type: 'area', height: 48, sparkline: { enabled: true }, animations: { enabled: false } };
  readonly sparkStroke: ApexStroke = { curve: 'smooth', width: 1.5 };
  readonly sparkFill: ApexFill = { type: 'gradient', gradient: { opacityFrom: 0.3, opacityTo: 0 } };
}
