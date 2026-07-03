import { Component, OnInit, computed, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, Gauge } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiTableComponent } from '../../shared/ui';

/** A named drawdown tolerance band (see CONTEXT.md: Risk Level). */
interface RiskLevel { label: string; pct: number; color: string; }

// Fixed bands, green → red by severity. Colors follow the app's profit/loss palette
// plus intermediate hues; risk tint is decorative here, the % + label carry meaning.
const LEVELS: RiskLevel[] = [
  { label: 'Very low', pct: 15, color: '#30a46c' },   // green
  { label: 'Low', pct: 20, color: '#3b9eff' },        // blue
  { label: 'Low–medium', pct: 30, color: '#f5a623' }, // amber
  { label: 'Medium', pct: 45, color: '#f76b15' },     // orange
  { label: 'Medium', pct: 50, color: '#e5484d' },     // red
];

const LS_CAPITAL = 'ph_risk_capital';
const LS_CUSTOM = 'ph_risk_custom_pct';

/**
 * Risk Level calculator — Risk Budget = capital × DD% for every band at once,
 * plus a custom percentage. Pure planning aid: capital is typed in (persisted in
 * localStorage only), nothing touches accounts or the backend besides the FX rate.
 */
@Component({
  selector: 'ph-risk',
  standalone: true,
  imports: [FormsModule, DecimalPipe, LucideAngularModule, UiCardComponent, UiTableComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <header class="flex flex-col gap-1">
        <h1 class="text-xl font-semibold tracking-tight">Risk Level</h1>
        <p class="text-sm text-text-muted">งบความเสี่ยง = ทุน × DD% — เครื่องมือวางแผน ไม่ผูกกับบัญชี/ข้อมูลเทรดจริง</p>
      </header>

      <!-- Capital input -->
      <ui-card>
        <div class="flex flex-wrap items-end gap-4">
          <label class="flex flex-col gap-1 text-xs text-text-muted">ทุน (USD)
            <input type="number" min="0" [ngModel]="capital()" (ngModelChange)="setCapital($event)"
                   class="h-10 w-44 rounded-md border border-border bg-surface-raised px-3 text-right text-base font-semibold tabular-nums text-text
                          focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
          </label>
          @if (thb(capital()); as t) {
            <span class="pb-2 text-xs tabular-nums text-text-faint">{{ t }}</span>
          }
        </div>
      </ui-card>

      <!-- Risk levels table -->
      <ui-card [padded]="false">
        <ui-table>
          <table>
            <thead><tr>
              <th>ระดับความเสี่ยง</th>
              <th class="!text-right">DD%</th>
              <th class="!text-right">งบความเสี่ยง (USD)</th>
              <th class="!text-right">฿</th>
            </tr></thead>
            <tbody>
              @for (l of levels; track l.pct) {
                <tr>
                  <td>
                    <span class="inline-flex items-center gap-2">
                      <span class="h-2.5 w-2.5 rounded-full" [style.background]="l.color"></span>
                      <span class="font-medium">{{ l.label }}</span>
                    </span>
                  </td>
                  <td class="text-right tabular-nums text-text-muted">{{ l.pct }}%</td>
                  <td class="text-right tabular-nums font-semibold" [style.color]="l.color">{{ budget(l.pct) | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums text-xs text-text-faint">{{ thb(budget(l.pct)) }}</td>
                </tr>
              }
              <!-- Custom percentage row -->
              <tr class="border-t-2 border-border">
                <td>
                  <span class="inline-flex items-center gap-2">
                    <lucide-icon [img]="icons.Gauge" class="h-4 w-4 text-brand-300"></lucide-icon>
                    <span class="font-medium">กำหนดเอง</span>
                  </span>
                </td>
                <td class="text-right">
                  <input type="number" min="0" max="100" [ngModel]="customPct()" (ngModelChange)="setCustomPct($event)"
                         class="h-8 w-20 rounded-md border border-border bg-surface-raised px-2 text-right text-sm tabular-nums
                                focus:outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30" />
                </td>
                <td class="text-right tabular-nums font-semibold text-brand-300">{{ budget(customPct()) | number:'1.2-2' }}</td>
                <td class="text-right tabular-nums text-xs text-text-faint">{{ thb(budget(customPct())) }}</td>
              </tr>
            </tbody>
          </table>
        </ui-table>
      </ui-card>
    </div>
  `,
})
export class RiskComponent implements OnInit {
  readonly levels = LEVELS;
  readonly icons = { Gauge };
  capital = signal<number>(Number(localStorage.getItem(LS_CAPITAL)) || 5500);
  customPct = signal<number>(Number(localStorage.getItem(LS_CUSTOM)) || 25);
  private fxRate = signal<number | null>(null);

  constructor(private api: ApiService) {}

  ngOnInit() {
    // FX rate for the THB secondary line — same source the other pages use.
    firstValueFrom(this.api.get<{ rate: number | null }>('/api/fx'))
      .then(fx => this.fxRate.set(fx.rate)).catch(() => this.fxRate.set(null));
  }

  setCapital(v: number) {
    this.capital.set(Number(v) || 0);
    localStorage.setItem(LS_CAPITAL, String(this.capital()));
  }

  setCustomPct(v: number) {
    this.customPct.set(Math.min(100, Math.max(0, Number(v) || 0)));
    localStorage.setItem(LS_CUSTOM, String(this.customPct()));
  }

  /** Risk Budget = capital × pct (see CONTEXT.md). */
  budget(pct: number): number {
    return (this.capital() * pct) / 100;
  }

  /** THB secondary, '' when no rate. */
  thb(usd: number): string {
    const r = this.fxRate();
    if (r == null || !usd) return '';
    return '≈ ฿' + (usd * r).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
}
