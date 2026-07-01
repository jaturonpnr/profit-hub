import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, Banknote, Check, Trash2, TriangleAlert } from 'lucide-angular';
import { ApiService } from '../../core/api.service';
import { UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent, UiButtonComponent } from '../../shared/ui';

interface PlanRow {
  accountId: string; name: string; capital: number; netProfit: number;
  suggestedAmount: number; periodFrom: string; periodTo: string;
}
interface WithdrawalRow {
  id: number; accountId: string; accountName: string; amount: number; withdrawnOn: string;
  suggestedAmount: number; periodFrom: string; periodTo: string; capital: number; note: string;
}
// Local editable state per plan row.
interface Editable extends PlanRow { amount: number; note: string; date: string; saving: boolean; }

/** Withdrawal calculator + log. Suggests each account's period Net Profit as the amount
 *  to withdraw; user edits + saves a Withdrawal Record. Does NOT affect Balance/ROI. */
@Component({
  selector: 'ph-withdrawals',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe, LucideAngularModule, UiCardComponent, UiBadgeComponent, UiTableComponent, UiSpinnerComponent, UiButtonComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6">
      <header class="flex flex-col gap-1">
        <h1 class="text-xl font-semibold tracking-tight">ถอนเงิน</h1>
        <p class="text-sm text-text-muted">คำนวณยอดถอน (แนะนำ = กำไรของช่วงที่เลือก) แก้ได้แล้วบันทึกเป็นประวัติ — ไม่กระทบ Balance/ROI จริง</p>
      </header>

      <!-- Period controls -->
      <ui-card>
        <div class="flex flex-wrap items-end gap-3">
          <label class="flex flex-col gap-1 text-xs text-text-muted">จากวันที่
            <input type="date" [(ngModel)]="from" (change)="reloadPlan()" class="h-9 rounded-md border border-border bg-surface-raised px-2.5 text-sm text-text" />
          </label>
          <label class="flex flex-col gap-1 text-xs text-text-muted">ถึงวันที่
            <input type="date" [(ngModel)]="to" (change)="reloadPlan()" class="h-9 rounded-md border border-border bg-surface-raised px-2.5 text-sm text-text" />
          </label>
          <button uiButton variant="secondary" (click)="thisMonth()">เดือนนี้</button>
        </div>
      </ui-card>

      <!-- Calculator table -->
      <ui-card [padded]="false">
        @if (loading()) { <ui-spinner label="กำลังคำนวณ…" /> }
        @else {
        <ui-table>
          <table>
            <thead><tr>
              <th>บัญชี</th>
              <th class="!text-right">ทุน</th>
              <th class="!text-right">กำไรช่วงนี้</th>
              <th class="!text-right">ยอดถอน</th>
              <th>โน้ต</th>
              <th class="!text-right">วันที่ถอน</th>
              <th></th>
            </tr></thead>
            <tbody>
              @for (r of rows(); track r.accountId) {
                <tr>
                  <td class="font-medium">{{ r.name }}</td>
                  <td class="text-right">
                    <input type="number" [(ngModel)]="r.capital" class="h-8 w-24 rounded-md border border-border bg-surface-raised px-2 text-right text-sm tabular-nums" />
                  </td>
                  <td class="text-right tabular-nums" [class.text-profit]="r.suggestedAmount > 0">{{ r.suggestedAmount | number:'1.2-2' }}</td>
                  <td class="text-right">
                    <input type="number" [(ngModel)]="r.amount" class="h-8 w-28 rounded-md border border-border bg-surface-raised px-2 text-right text-sm tabular-nums"
                           [class.border-loss]="r.amount > r.suggestedAmount" />
                    @if (r.amount > r.suggestedAmount) {
                      <div class="mt-0.5 flex items-center justify-end gap-1 text-[11px] text-loss">
                        <lucide-icon [img]="icons.TriangleAlert" class="h-3 w-3"></lucide-icon> เกินกำไร (แตะทุน)
                      </div>
                    }
                  </td>
                  <td><input [(ngModel)]="r.note" placeholder="โน้ต" class="h-8 w-32 rounded-md border border-border bg-surface-raised px-2 text-sm" /></td>
                  <td class="text-right"><input type="date" [(ngModel)]="r.date" class="h-8 rounded-md border border-border bg-surface-raised px-2 text-sm" /></td>
                  <td class="text-right">
                    <button uiButton variant="primary" size="sm" [disabled]="r.saving || r.amount <= 0" (click)="save(r)">บันทึก</button>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7"><div class="py-10 text-center text-sm text-text-faint">ยังไม่มีบัญชี</div></td></tr>
              }
            </tbody>
          </table>
        </ui-table>
        }
      </ui-card>

      <!-- History -->
      <ui-card [hasHeader]="true" [padded]="false">
        <div uiCardHeader><h2 class="text-sm font-medium text-text-muted">ประวัติการถอน</h2></div>
        <ui-table>
          <table>
            <thead><tr>
              <th>วันที่</th><th>บัญชี</th><th class="!text-right">ยอดถอน</th>
              <th class="!text-right">ยอดแนะนำ</th><th>ช่วงกำไร</th><th>โน้ต</th><th></th>
            </tr></thead>
            <tbody>
              @for (h of history(); track h.id) {
                <tr>
                  <td class="tabular-nums text-text-muted">{{ h.withdrawnOn | date:'yyyy-MM-dd' }}</td>
                  <td>{{ h.accountName }}</td>
                  <td class="text-right tabular-nums font-medium">{{ h.amount | number:'1.2-2' }}</td>
                  <td class="text-right tabular-nums text-text-muted">{{ h.suggestedAmount | number:'1.2-2' }}</td>
                  <td class="text-xs tabular-nums text-text-faint">{{ h.periodFrom | date:'yyyy-MM-dd' }} → {{ h.periodTo | date:'yyyy-MM-dd' }}</td>
                  <td class="text-text-muted">{{ h.note }}</td>
                  <td class="text-right"><button (click)="remove(h)" class="p-1 text-text-faint hover:text-loss" aria-label="ลบ"><lucide-icon [img]="icons.Trash2" class="h-4 w-4"></lucide-icon></button></td>
                </tr>
              } @empty {
                <tr><td colspan="7"><div class="py-8 text-center text-sm text-text-faint">ยังไม่มีประวัติ</div></td></tr>
              }
            </tbody>
          </table>
        </ui-table>
      </ui-card>
    </div>

    @if (saved()) {
      <div class="fixed bottom-5 left-1/2 z-50 -translate-x-1/2">
        <ui-badge variant="brand"><lucide-icon [img]="icons.Check" class="mr-1 inline h-3.5 w-3.5"></lucide-icon>บันทึกแล้ว</ui-badge>
      </div>
    }
  `,
})
export class WithdrawalsComponent implements OnInit {
  rows = signal<Editable[]>([]);
  history = signal<WithdrawalRow[]>([]);
  loading = signal(true);
  saved = signal(false);
  from = '';
  to = '';
  readonly icons = { Banknote, Check, Trash2, TriangleAlert };

  constructor(private api: ApiService) {}

  async ngOnInit() {
    const now = new Date();
    this.from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
    this.to = now.toISOString().slice(0, 10);
    await Promise.all([this.reloadPlan(), this.reloadHistory()]);
  }

  thisMonth() {
    const now = new Date();
    this.from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
    this.to = now.toISOString().slice(0, 10);
    this.reloadPlan();
  }

  async reloadPlan() {
    this.loading.set(true);
    try {
      const plan = await firstValueFrom(this.api.get<PlanRow[]>('/api/withdrawals/plan', { from: this.from, to: this.to }));
      const today = new Date().toISOString().slice(0, 10);
      this.rows.set(plan.map(p => ({ ...p, amount: p.suggestedAmount, note: '', date: today, saving: false })));
    } finally {
      this.loading.set(false);
    }
  }

  async reloadHistory() {
    this.history.set(await firstValueFrom(this.api.get<WithdrawalRow[]>('/api/withdrawals')));
  }

  async save(r: Editable) {
    if (r.amount <= 0) return;
    r.saving = true;
    try {
      await firstValueFrom(this.api.post('/api/withdrawals', {
        accountId: r.accountId, amount: r.amount, withdrawnOn: r.date,
        suggestedAmount: r.suggestedAmount, periodFrom: r.periodFrom, periodTo: r.periodTo,
        capital: r.capital, note: r.note,
      }));
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 1500);
      await this.reloadHistory();
    } finally {
      r.saving = false;
    }
  }

  async remove(h: WithdrawalRow) {
    if (!confirm(`ลบรายการถอน ${h.amount} ของ ${h.accountName}?`)) return;
    await firstValueFrom(this.api.delete(`/api/withdrawals/${h.id}`));
    await this.reloadHistory();
  }
}
