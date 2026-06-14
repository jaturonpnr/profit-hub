import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Clock, Check } from 'lucide-angular';
import { AuthService } from '../../core/auth.service';
import { UiCardComponent, UiButtonComponent, UiBadgeComponent } from '../../shared/ui';

interface ZoneOption { id: string; label: string; }

/**
 * Settings — lets the user pick the reporting timezone used to bucket
 * day/week/month report rows. Saving issues a fresh JWT (new `tz` claim) which
 * the dashboard re-reads, so dates line up with the MT5 broker server time.
 */
@Component({
  selector: 'ph-settings',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, UiCardComponent, UiButtonComponent, UiBadgeComponent],
  template: `
    <div class="animate-fade-in flex flex-col gap-6 max-w-2xl">
      <div>
        <h1 class="text-xl font-semibold tracking-tight">Settings</h1>
        <p class="text-sm text-text-muted mt-0.5">Configure how your reports are grouped.</p>
      </div>

      <ui-card [hasHeader]="true">
        <div uiCardHeader class="flex items-center gap-2">
          <div class="h-7 w-7 rounded-md bg-brand-500/10 border border-brand-500/20 grid place-items-center">
            <lucide-icon [img]="icons.Clock" class="h-4 w-4 text-brand-300"></lucide-icon>
          </div>
          <h2 class="text-sm font-medium">Reporting timezone</h2>
        </div>

        <div class="flex flex-col gap-4">
          <label class="flex flex-col gap-1.5">
            <span class="text-xs font-medium text-text-muted">Timezone</span>
            <select
              [(ngModel)]="selected"
              class="w-full rounded-md bg-surface-raised border border-border px-3 py-2 text-sm text-text
                     focus:outline-none focus:ring-2 focus:ring-brand-500/40 focus:border-brand-500"
            >
              @for (z of zones; track z.id) {
                <option [value]="z.id">{{ z.label }}</option>
              }
            </select>
          </label>

          <p class="text-xs text-text-muted leading-relaxed">
            ตัวเลือกนี้กำหนดวิธีจัดกลุ่มรายงานเป็นรายวัน / รายสัปดาห์ / รายเดือน
            ควรตั้งให้ตรงกับเวลาเซิร์ฟเวอร์ของโบรกเกอร์ MT5 เพื่อให้วันที่บนแดชบอร์ดตรงกับใน MT5
            (เทรดถูกเก็บเป็น UTC จริง และจะถูกแปลงตามโซนเวลานี้)
          </p>

          <div class="flex items-center gap-3">
            <button uiButton variant="primary" (click)="save()" [disabled]="saving()">
              {{ saving() ? 'Saving…' : 'Save' }}
            </button>
            @if (saved()) {
              <ui-badge variant="profit">
                <lucide-icon [img]="icons.Check" class="h-3 w-3"></lucide-icon>
                Saved
              </ui-badge>
            }
          </div>
        </div>
      </ui-card>
    </div>
  `,
})
export class SettingsComponent implements OnInit {
  readonly icons = { Clock, Check };

  readonly zones: ZoneOption[] = [
    { id: 'UTC', label: 'UTC' },
    { id: 'Asia/Bangkok', label: 'Asia/Bangkok (เวลาไทย, UTC+7)' },
    { id: 'Europe/Athens', label: 'Europe/Athens (Broker EET/EEST, UTC+2/+3)' },
    { id: 'Etc/GMT-2', label: 'Etc/GMT-2 (Broker fixed UTC+2)' },
    { id: 'Etc/GMT-3', label: 'Etc/GMT-3 (Broker fixed UTC+3)' },
    { id: 'Europe/London', label: 'Europe/London (UTC+0/+1)' },
    { id: 'America/New_York', label: 'America/New_York (UTC-5/-4)' },
  ];

  selected = 'Asia/Bangkok';
  saving = signal(false);
  saved = signal(false);

  constructor(private auth: AuthService) {}

  async ngOnInit() {
    try {
      const me = await this.auth.getMe();
      this.selected = me.timeZone;
    } catch {
      this.selected = this.auth.timeZone;
    }
  }

  async save() {
    this.saving.set(true);
    this.saved.set(false);
    try {
      await this.auth.setTimeZone(this.selected);
      this.saved.set(true);
      // Reload so the dashboard re-reads the new tz claim from the stored token.
      setTimeout(() => location.assign('/'), 600);
    } finally {
      this.saving.set(false);
    }
  }
}
