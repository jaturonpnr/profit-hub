import { Component, OnInit, signal, computed } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  NgApexchartsModule, ApexChart, ApexAxisChartSeries, ApexFill, ApexStroke,
  ApexDataLabels, ApexGrid, ApexXAxis, ApexYAxis, ApexTooltip,
} from 'ng-apexcharts';
import { LucideAngularModule, ArrowLeft, ChevronRight, Pencil, Plus, Trash2 } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiSpinnerComponent, UiButtonComponent } from '../../shared/ui';
import { BacktestSummary } from './backtests.component';

interface EquityPoint { t: string; balance: number; }
interface InputEntry { section: string; key: string; value: string; }
/** User-defined readable name + per-value texts for one raw input key (Input Label, see CONTEXT.md). */
interface InputLabelRow { key: string; label: string; valueMap: Record<string, string>; }
interface HeatCell { dow: number; hour: number; netProfit: number; tradeCount: number; }
interface MonthlyRow { month: string; netProfit: number; tradeCount: number; }
interface BtTrade { t: string; dir: string; lots: number; profit: number; } // ISO broker time
interface DayRow { day: string; netProfit: number; tradeCount: number; }

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
  imports: [DecimalPipe, DatePipe, FormsModule, RouterLink, NgApexchartsModule, LucideAngularModule, UiCardComponent, UiSpinnerComponent, UiButtonComponent],
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
            <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">กำไรรายเดือน (backtest) — คลิกเดือนเพื่อดูรายวัน, คลิกวันเพื่อดูรายไม้</h2></div>
            <div class="overflow-x-auto">
              <table class="w-full text-xs">
                <thead><tr class="text-text-faint"><th class="px-3 py-2 text-left">เดือน</th><th class="px-3 py-2 text-right">Net Profit</th><th class="px-3 py-2 text-right">ไม้</th></tr></thead>
                <tbody>
                  @for (m of monthly(); track m.month) {
                    <!-- Month: the only level with a divider line; expanded month gets a brand tint. -->
                    <tr class="cursor-pointer border-t border-border-subtle transition-colors"
                        [class]="expandedMonth() === m.month ? 'bg-brand-500/[0.07]' : 'hover:bg-surface-raised/60'"
                        (click)="toggleMonth(m.month)">
                      <td class="px-3 py-2 tabular-nums font-medium" [class.text-brand-300]="expandedMonth() === m.month" [class.text-text]="expandedMonth() !== m.month">
                        <span class="inline-flex items-center gap-1.5">
                          <lucide-icon [img]="icons.ChevronRight" class="h-3.5 w-3.5 text-text-faint transition-transform duration-200" [class.rotate-90]="expandedMonth() === m.month"></lucide-icon>
                          {{ m.month }}
                        </span>
                      </td>
                      <td class="px-3 py-2 text-right tabular-nums font-semibold" [class.text-profit]="m.netProfit >= 0" [class.text-loss]="m.netProfit < 0">{{ m.netProfit | number:'1.2-2' }}</td>
                      <td class="px-3 py-2 text-right tabular-nums text-text-faint">{{ m.tradeCount }}</td>
                    </tr>
                    @if (expandedMonth() === m.month) {
                      <!-- Days: no dividers — one soft shaded band + brand left rail groups the whole month. -->
                      @for (d of daysOf(m.month); track d.day) {
                        <tr class="cursor-pointer bg-white/[0.02] transition-colors"
                            [class]="expandedDay() === d.day ? 'bg-white/[0.05]' : 'hover:bg-white/[0.04]'"
                            (click)="toggleDay(d.day)">
                          <td class="border-l-2 border-brand-500/40 py-1.5 pl-9 pr-3 tabular-nums text-text-muted">
                            <span class="inline-flex items-center gap-1.5">
                              <lucide-icon [img]="icons.ChevronRight" class="h-3 w-3 text-text-faint/60 transition-transform duration-200" [class.rotate-90]="expandedDay() === d.day"></lucide-icon>
                              <span class="text-text-faint">{{ d.day.slice(0, 8) }}</span><span class="font-medium text-text-muted">{{ d.day.slice(8) }}</span>
                            </span>
                          </td>
                          <td class="py-1.5 px-3 text-right tabular-nums" [class.text-profit]="d.netProfit >= 0" [class.text-loss]="d.netProfit < 0">{{ d.netProfit | number:'1.2-2' }}</td>
                          <td class="py-1.5 px-3 text-right tabular-nums text-text-faint">{{ d.tradeCount }}</td>
                        </tr>
                        @if (expandedDay() === d.day) {
                          <!-- Trades: deepest shade, no lines, muted — whitespace does the separation. -->
                          @for (t of tradesOf(d.day); track $index) {
                            <tr class="bg-white/[0.05]">
                              <td class="border-l-2 border-brand-500/40 py-1 pl-[3.75rem] pr-3 font-mono text-[11px] text-text-faint">{{ t.time }}</td>
                              <td class="py-1 px-3 text-right">
                                <span class="mr-2 rounded px-1.5 py-px text-[10px] font-semibold uppercase tracking-wide"
                                      [class]="t.dir === 'buy' ? 'bg-profit/10 text-profit/90' : 'bg-loss/10 text-loss/90'">{{ t.dir }}</span>
                                <span class="tabular-nums" [class.text-profit]="t.profit >= 0" [class.text-loss]="t.profit < 0">{{ t.profit | number:'1.2-2' }}</span>
                              </td>
                              <td class="py-1 px-3 text-right tabular-nums text-[11px] text-text-faint">{{ t.lots | number:'1.2-2' }} lots</td>
                            </tr>
                          }
                        }
                      }
                    }
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
                          <td class="py-1 pr-4">
                            @if (labelOf(i.key); as l) {
                              <div class="text-sm text-text">{{ l }}</div>
                              <div class="font-mono text-[10px] text-text-faint">{{ i.key }}</div>
                            } @else {
                              <span class="font-mono text-xs text-text-muted">{{ i.key }}</span>
                            }
                          </td>
                          <td class="py-1 text-right tabular-nums">
                            @if (valueTextOf(i.key, i.value); as vt) {
                              {{ vt }} <span class="text-[10px] text-text-faint">({{ i.value }})</span>
                            } @else {
                              {{ i.value }}
                            }
                          </td>
                          <td class="w-8 py-1 pl-3 text-right">
                            <button type="button" aria-label="ตั้งชื่อ" (click)="openEditor(i)"
                              class="text-text-faint hover:text-brand-300 transition-colors">
                              <lucide-icon [img]="icons.Pencil" class="h-3.5 w-3.5"></lucide-icon>
                            </button>
                          </td>
                        </tr>
                        @if (editingKey() === i.key) {
                          <tr class="border-b border-border/40 last:border-0">
                            <td colspan="3" class="py-3">
                              <div class="flex flex-col gap-2 rounded-md border border-border bg-surface-raised/40 p-3">
                                <input [(ngModel)]="editLabel" placeholder="ชื่อที่อ่านง่าย" [attr.aria-label]="'ชื่อที่อ่านง่ายของ ' + i.key"
                                  class="h-8 w-full rounded-md border border-border bg-surface-raised px-2.5 text-sm font-medium text-text placeholder:text-text-faint
                                         focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
                                @for (p of editPairs; track $index) {
                                  <div class="flex items-center gap-2">
                                    <input [(ngModel)]="p.k" placeholder="ค่า"
                                      class="h-8 w-24 rounded-md border border-border bg-surface-raised px-2.5 text-sm text-text placeholder:text-text-faint
                                             focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
                                    <input [(ngModel)]="p.v" placeholder="ข้อความ"
                                      class="h-8 min-w-0 flex-1 rounded-md border border-border bg-surface-raised px-2.5 text-sm text-text placeholder:text-text-faint
                                             focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
                                    <button type="button" aria-label="ลบค่า" (click)="removePair($index)"
                                      class="text-text-faint hover:text-loss transition-colors">
                                      <lucide-icon [img]="icons.Trash2" class="h-3.5 w-3.5"></lucide-icon>
                                    </button>
                                  </div>
                                }
                                <div class="flex items-center justify-between gap-2">
                                  <button uiButton type="button" variant="ghost" size="sm" (click)="addPair()">
                                    <lucide-icon [img]="icons.Plus" class="h-3.5 w-3.5"></lucide-icon> เพิ่มค่า
                                  </button>
                                  <div class="flex items-center gap-2">
                                    <button uiButton type="button" variant="ghost" size="sm" (click)="closeEditor()">ยกเลิก</button>
                                    <button uiButton type="button" variant="primary" size="sm" [disabled]="saving()" (click)="saveLabel(i.key)">บันทึก</button>
                                  </div>
                                </div>
                              </div>
                            </td>
                          </tr>
                        }
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
  private trades = signal<BtTrade[]>([]);
  expandedMonth = signal<string | null>(null);
  expandedDay = signal<string | null>(null);
  // Input Labels: readable names/value texts per raw key, global per user.
  labels = signal<Map<string, InputLabelRow>>(new Map());
  editingKey = signal<string | null>(null);
  saving = signal(false);
  editLabel = '';
  editPairs: { k: string; v: string }[] = [];
  readonly icons = { ArrowLeft, ChevronRight, Pencil, Plus, Trash2 };
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
      const [d, labelRows] = await Promise.all([
        firstValueFrom(this.api.get<{
          summary: BacktestSummary; equityCurve: EquityPoint[]; inputs: InputEntry[];
          tradeStats: Record<string, string>; heatmap: HeatCell[]; monthly: MonthlyRow[];
          trades: BtTrade[];
        }>(`/api/backtests/${id}`)),
        // Labels are decoration — a failed fetch must not break the page.
        firstValueFrom(this.api.get<InputLabelRow[]>('/api/input-labels')).catch(() => [] as InputLabelRow[]),
      ]);
      this.labels.set(new Map(labelRows.map(l => [l.key, l])));
      this.s.set(d.summary);
      this.points.set(d.equityCurve);
      this.inputs.set(d.inputs ?? []);
      this.tradeStats.set(d.tradeStats ?? {});
      this.heatmap.set(d.heatmap ?? []);
      this.monthly.set(d.monthly ?? []);
      this.trades.set(d.trades ?? []);
    } finally {
      this.loading.set(false);
    }
  }

  // ── Input Labels: display lookups + inline editor ──
  labelOf(key: string): string | null {
    const label = this.labels().get(key)?.label;
    return label ? label : null;                 // "" → fall back to raw key
  }
  valueTextOf(key: string, value: string): string | null {
    return this.labels().get(key)?.valueMap[value] ?? null;
  }

  openEditor(i: InputEntry) {
    if (this.editingKey() === i.key) { this.closeEditor(); return; }
    const row = this.labels().get(i.key);
    this.editLabel = row?.label ?? '';
    this.editPairs = Object.entries(row?.valueMap ?? {}).map(([k, v]) => ({ k, v }));
    // Convenience: no mapping yet + numeric raw value → prefill one pair to name.
    if (this.editPairs.length === 0 && /^-?\d+(\.\d+)?$/.test(i.value.trim())) {
      this.editPairs = [{ k: i.value.trim(), v: '' }];
    }
    this.editingKey.set(i.key);
  }
  closeEditor() { this.editingKey.set(null); }
  addPair() { this.editPairs.push({ k: '', v: '' }); }
  removePair(idx: number) { this.editPairs.splice(idx, 1); }

  async saveLabel(key: string) {
    const label = this.editLabel.trim();
    const valueMap: Record<string, string> = {};
    for (const p of this.editPairs) {
      const k = p.k.trim(), v = p.v.trim();
      if (k && v) valueMap[k] = v;               // drop incomplete pairs
    }
    this.saving.set(true);
    try {
      await firstValueFrom(this.api.put(`/api/input-labels/${encodeURIComponent(key)}`, { label, valueMap }));
      const m = new Map(this.labels());
      if (!label && Object.keys(valueMap).length === 0) m.delete(key); // empty payload = backend deleted it
      else m.set(key, { key, label, valueMap });
      this.labels.set(m);
      this.closeEditor();
    } catch {
      // keep the editor open so the user can retry
    } finally {
      this.saving.set(false);
    }
  }

  // ── Month → day → trades drill-down (broker time; ISO strings sliced, no Date parsing) ──
  toggleMonth(month: string) {
    this.expandedMonth.set(this.expandedMonth() === month ? null : month);
    this.expandedDay.set(null); // collapse day level when switching months
  }
  toggleDay(day: string) {
    this.expandedDay.set(this.expandedDay() === day ? null : day);
  }

  // O(1) template lookups, rebuilt only when trades change.
  private byDay = computed(() => {
    const m = new Map<string, BtTrade[]>();
    for (const t of this.trades()) {
      const day = t.t.slice(0, 10);          // "2026-05-04"
      if (!m.has(day)) m.set(day, []);
      m.get(day)!.push(t);
    }
    return m;
  });
  private dayRowsByMonth = computed(() => {
    const m = new Map<string, DayRow[]>();
    for (const [day, list] of this.byDay()) {
      const month = day.slice(0, 7);          // "2026-05"
      if (!m.has(month)) m.set(month, []);
      m.get(month)!.push({
        day,
        netProfit: Math.round(list.reduce((s, t) => s + t.profit, 0) * 100) / 100,
        tradeCount: list.length,
      });
    }
    for (const rows of m.values()) rows.sort((a, b) => a.day.localeCompare(b.day));
    return m;
  });

  daysOf(month: string): DayRow[] { return this.dayRowsByMonth().get(month) ?? []; }
  tradesOf(day: string) {
    return (this.byDay().get(day) ?? []).map(t => ({ ...t, time: t.t.slice(11, 19) }));
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
