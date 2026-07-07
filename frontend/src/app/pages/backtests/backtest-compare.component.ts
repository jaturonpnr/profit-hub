import { Component, OnInit, signal, computed } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, ArrowLeft } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiSpinnerComponent, UiButtonComponent } from '../../shared/ui';
import { BacktestSummary } from './backtests.component';

interface InputEntry { section: string; key: string; value: string; }
interface Detail { summary: BacktestSummary; inputs: InputEntry[]; }

/** One KPI comparison row: label + formatted values + which side is better. */
interface KpiRow { label: string; a: string; b: string; better: 'a' | 'b' | null; }

/** One inputs-diff row: union of both sides' keys (A's order first, then B-only). */
interface DiffRow { section: string; key: string; a: string | null; b: string | null; diff: boolean; }

/**
 * Backtest settings compare — /backtests/compare?a=&b=. KPIs side-by-side
 * (better side in text-profit) + EA Inputs diff, differences first by default.
 */
@Component({
  selector: 'ph-backtest-compare',
  standalone: true,
  imports: [RouterLink, LucideAngularModule, UiCardComponent, UiSpinnerComponent, UiButtonComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <a routerLink="/backtests" class="inline-flex items-center gap-1.5 text-sm text-text-muted hover:text-text transition-colors">
        <lucide-icon [img]="icons.ArrowLeft" class="h-4 w-4"></lucide-icon> Back to backtests
      </a>

      @if (loading()) {
        <ui-spinner label="Loading comparison…" />
      } @else if (a() === null || b() === null) {
        <div class="py-12 text-center text-sm text-text-faint">ไม่พบ backtest ที่เลือก — กลับไปเลือกใหม่จากหน้ารายการ</div>
      } @else {
        <header class="flex flex-col gap-1">
          <h1 class="text-xl font-semibold tracking-tight">เทียบ Backtest Settings</h1>
          <p class="text-sm text-text-muted">{{ a()!.summary.expertName }} vs {{ b()!.summary.expertName }}</p>
        </header>

        <ui-card [hasHeader]="true">
          <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">KPIs</h2></div>
          <div class="overflow-x-auto px-4 pb-4 pt-3">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-border text-left">
                  <th class="py-2 pr-4 font-medium text-text-faint"></th>
                  <th class="py-2 pr-4 text-right font-medium">
                    {{ a()!.summary.expertName }}
                    <div class="text-[11px] font-normal text-text-faint">{{ a()!.summary.sourceFileName }}</div>
                  </th>
                  <th class="py-2 text-right font-medium">
                    {{ b()!.summary.expertName }}
                    <div class="text-[11px] font-normal text-text-faint">{{ b()!.summary.sourceFileName }}</div>
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (r of kpiRows(); track r.label) {
                  <tr class="border-b border-border/40 last:border-0">
                    <td class="py-1.5 pr-4 text-text-muted">{{ r.label }}</td>
                    <td class="py-1.5 pr-4 text-right tabular-nums" [class.text-profit]="r.better === 'a'" [class.font-semibold]="r.better === 'a'">{{ r.a }}</td>
                    <td class="py-1.5 text-right tabular-nums" [class.text-profit]="r.better === 'b'" [class.font-semibold]="r.better === 'b'">{{ r.b }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </ui-card>

        <ui-card [hasHeader]="true">
          <div uiCardHeader class="flex items-center justify-between gap-4">
            <h2 class="text-sm font-medium text-text-muted">EA Inputs diff</h2>
            @if (!missingInputs()) {
              <button uiButton variant="secondary" size="sm" (click)="showAll.set(!showAll())">
                {{ showAll() ? 'เฉพาะที่ต่าง' : 'แสดงทั้งหมด' }}
              </button>
            }
          </div>
          <div class="px-4 pb-4 pt-3">
            @if (missingInputs()) {
              <p class="py-6 text-center text-sm text-text-faint">ไฟล์นี้อัปโหลดก่อนระบบเก็บ EA Inputs — อัปโหลดไฟล์ใหม่อีกครั้งเพื่อเทียบ</p>
            } @else if (visibleRows().length === 0) {
              <p class="py-6 text-center text-sm text-text-faint">EA Inputs เหมือนกันทุกค่า</p>
            } @else {
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <tbody>
                    @for (r of visibleRows(); track r.key) {
                      @if (r.section !== previousSection(visibleRows(), $index)) {
                        <tr>
                          <td colspan="3" class="pb-1 pt-3 text-xs uppercase tracking-wide text-text-faint">{{ r.section || 'Settings' }}</td>
                        </tr>
                      }
                      <tr class="border-b border-border/40 last:border-0"
                          [class]="r.diff ? 'bg-brand-500/10 border-l-2 border-l-brand-500' : ''">
                        <td class="py-1 pl-2 pr-4 font-mono text-xs text-text-muted">{{ r.key }}</td>
                        <td class="py-1 pr-4 text-right tabular-nums">{{ r.a ?? '—' }}</td>
                        <td class="py-1 pr-2 text-right tabular-nums">{{ r.b ?? '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </ui-card>
      }
    </div>
  `,
})
export class BacktestCompareComponent implements OnInit {
  a = signal<Detail | null>(null);
  b = signal<Detail | null>(null);
  loading = signal(true);
  showAll = signal(false);
  readonly icons = { ArrowLeft };

  constructor(private api: ApiService, private route: ActivatedRoute) {}

  async ngOnInit() {
    const q = this.route.snapshot.queryParamMap;
    const idA = q.get('a');
    const idB = q.get('b');
    try {
      if (idA && idB) {
        const [da, db] = await Promise.all([
          firstValueFrom(this.api.get<Detail>(`/api/backtests/${idA}`)),
          firstValueFrom(this.api.get<Detail>(`/api/backtests/${idB}`)),
        ]);
        this.a.set(da);
        this.b.set(db);
      }
    } catch {
      // either fetch failed → "not found" fallback (a()/b() stays null)
    } finally {
      this.loading.set(false);
    }
  }

  private readonly num = new DecimalPipe('en-US');
  private readonly date = new DatePipe('en-US');

  // KPI rows: better side highlighted; higher wins except Max Equity DD % (lower wins).
  kpiRows = computed<KpiRow[]>(() => {
    const A = this.a()?.summary;
    const B = this.b()?.summary;
    if (!A || !B) return [];
    const n = (v: number, fmt = '1.2-2') => this.num.transform(v, fmt) ?? '—';
    const cmp = (av: number, bv: number, lowerBetter = false): 'a' | 'b' | null =>
      av === bv ? null : (av > bv) !== lowerBetter ? 'a' : 'b';
    const period = (s: BacktestSummary) =>
      `${this.date.transform(s.periodFrom, 'yyyy-MM-dd') ?? '—'} → ${this.date.transform(s.periodTo, 'yyyy-MM-dd') ?? '—'}`;
    return [
      { label: 'Net Profit', a: `${n(A.netProfit)} ${A.currency}`, b: `${n(B.netProfit)} ${B.currency}`, better: cmp(A.netProfit, B.netProfit) },
      { label: 'Return %', a: `${n(A.returnPct, '1.1-1')}%`, b: `${n(B.returnPct, '1.1-1')}%`, better: cmp(A.returnPct, B.returnPct) },
      { label: 'Profit Factor', a: n(A.profitFactor), b: n(B.profitFactor), better: cmp(A.profitFactor, B.profitFactor) },
      { label: 'Max Equity DD %', a: `${n(A.equityDrawdownMaxPct)}%`, b: `${n(B.equityDrawdownMaxPct)}%`, better: cmp(A.equityDrawdownMaxPct, B.equityDrawdownMaxPct, true) },
      { label: 'Recovery Factor', a: n(A.recoveryFactor), b: n(B.recoveryFactor), better: cmp(A.recoveryFactor, B.recoveryFactor) },
      { label: 'Sharpe', a: n(A.sharpeRatio), b: n(B.sharpeRatio), better: cmp(A.sharpeRatio, B.sharpeRatio) },
      { label: 'Trades', a: `${A.totalTrades}`, b: `${B.totalTrades}`, better: cmp(A.totalTrades, B.totalTrades) },
      { label: 'Win %', a: `${n(A.winRatePct, '1.1-1')}%`, b: `${n(B.winRatePct, '1.1-1')}%`, better: cmp(A.winRatePct, B.winRatePct) },
      { label: 'Deposit', a: n(A.initialDeposit, '1.0-0'), b: n(B.initialDeposit, '1.0-0'), better: null },
      { label: 'Period', a: period(A), b: period(B), better: null },
    ];
  });

  missingInputs = computed(() =>
    (this.a()?.inputs?.length ?? 0) === 0 || (this.b()?.inputs?.length ?? 0) === 0);

  // Union of keys: A's order first, then B-only keys appended in B's order.
  diffRows = computed<DiffRow[]>(() => {
    const ia = this.a()?.inputs ?? [];
    const ib = this.b()?.inputs ?? [];
    const bByKey = new Map(ib.map(i => [i.key, i]));
    const rows: DiffRow[] = ia.map(i => {
      const other = bByKey.get(i.key);
      return { section: i.section || other?.section || '', key: i.key, a: i.value, b: other?.value ?? null, diff: i.value !== (other?.value ?? null) };
    });
    const seen = new Set(ia.map(i => i.key));
    for (const i of ib) {
      if (!seen.has(i.key)) rows.push({ section: i.section, key: i.key, a: null, b: i.value, diff: true });
    }
    return rows;
  });

  visibleRows = computed<DiffRow[]>(() =>
    this.showAll() ? this.diffRows() : this.diffRows().filter(r => r.diff));

  /** Section of the row before index i (or null) — drives group-header rows in the template. */
  previousSection(rows: DiffRow[], i: number): string | null {
    return i === 0 ? null : rows[i - 1].section;
  }
}
