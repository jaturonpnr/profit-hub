import { Component, ElementRef, EventEmitter, HostListener, OnInit, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LucideAngularModule, SlidersHorizontal, Calendar, ChevronDown, Check } from 'lucide-angular';
import { ApiService } from '../core/api.service';
import { FilterService, AccountInfo } from '../core/filter.service';

/**
 * ph-filter-bar — shared toolbar that sits above dashboard/trades content.
 *
 * Account multi-select is a Tailwind popover (button summary + checkbox panel),
 * EA is a styled native select, and the date range is two labelled date inputs.
 * Violet focus states, Lucide icons.
 *
 * Presentation only. Every FilterService signal write (selectedIds, magic, from,
 * to), the (changed) emits, and the account/EA/date logic are preserved verbatim
 * from the original implementation — only markup/styling changed.
 */
@Component({
  selector: 'ph-filter-bar',
  standalone: true,
  imports: [FormsModule, LucideAngularModule],
  template: `
    <div class="flex flex-wrap items-end gap-3 rounded-lg border border-border bg-surface p-3">
      <!-- Accounts multi-select popover -->
      <div class="relative flex flex-col gap-1">
        <span class="text-xs font-medium text-text-muted">Accounts</span>
        <button
          type="button"
          (click)="open.set(!open())"
          class="inline-flex h-9 min-w-[10rem] items-center justify-between gap-2 rounded-md border border-border bg-surface-raised px-3 text-sm text-text transition-colors hover:bg-border focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
        >
          <span class="inline-flex items-center gap-2">
            <lucide-icon [img]="icons.SlidersHorizontal" class="h-4 w-4 text-text-muted"></lucide-icon>
            {{ label() }}
          </span>
          <lucide-icon
            [img]="icons.ChevronDown"
            class="h-4 w-4 text-text-faint transition-transform"
            [class.rotate-180]="open()"
          ></lucide-icon>
        </button>

        @if (open()) {
          <div
            class="absolute left-0 top-[calc(100%+0.25rem)] z-20 max-h-72 w-60 overflow-auto rounded-md border border-border bg-surface-raised p-1 shadow-[var(--shadow-card)]"
          >
            @for (a of filter.accounts(); track a.id) {
              <label
                class="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm text-text hover:bg-surface"
                (click)="toggle(a.id); $event.preventDefault()"
              >
                <span
                  class="flex h-4 w-4 items-center justify-center rounded border"
                  [class.border-brand-500]="isSelected(a.id)"
                  [class.bg-brand-600]="isSelected(a.id)"
                  [class.border-border]="!isSelected(a.id)"
                >
                  @if (isSelected(a.id)) {
                    <lucide-icon [img]="icons.Check" class="h-3 w-3 text-white"></lucide-icon>
                  }
                </span>
                <span class="truncate tabular-nums">{{ a.accountNumber }}</span>
              </label>
            } @empty {
              <div class="px-2 py-2 text-xs text-text-faint">No accounts.</div>
            }
          </div>
        }
      </div>

      <!-- EA (account by name) select -->
      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-text-muted">EA</span>
        <div class="relative">
          <select
            [ngModel]="selectedAccountId()"
            (ngModelChange)="selectByName($event)"
            class="h-9 min-w-[9rem] appearance-none rounded-md border border-border bg-surface-raised pl-3 pr-8 text-sm text-text transition-colors hover:bg-border focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
          >
            <option [ngValue]="null">All EAs</option>
            @for (a of filter.accounts(); track a.id) {
              <option [ngValue]="a.id">{{ a.name || a.accountNumber }}</option>
            }
          </select>
          <lucide-icon
            [img]="icons.ChevronDown"
            class="pointer-events-none absolute right-2 top-1/2 h-4 w-4 -translate-y-1/2 text-text-faint"
          ></lucide-icon>
        </div>
      </div>

      <!-- Date range -->
      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-text-muted">From</span>
        <div class="relative">
          <lucide-icon
            [img]="icons.Calendar"
            class="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-text-faint"
          ></lucide-icon>
          <input
            type="date"
            [ngModel]="filter.from()"
            (ngModelChange)="filter.from.set($event); changed.emit()"
            class="h-9 rounded-md border border-border bg-surface-raised pl-8 pr-3 text-sm text-text transition-colors hover:bg-border focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 [color-scheme:dark]"
          />
        </div>
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-xs font-medium text-text-muted">To</span>
        <div class="relative">
          <lucide-icon
            [img]="icons.Calendar"
            class="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-text-faint"
          ></lucide-icon>
          <input
            type="date"
            [ngModel]="filter.to()"
            (ngModelChange)="filter.to.set($event); changed.emit()"
            class="h-9 rounded-md border border-border bg-surface-raised pl-8 pr-3 text-sm text-text transition-colors hover:bg-border focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 [color-scheme:dark]"
          />
        </div>
      </div>
    </div>
  `,
})
export class FilterBarComponent implements OnInit {
  @Output() changed = new EventEmitter<void>();
  open = signal(false);
  readonly icons = { SlidersHorizontal, Calendar, ChevronDown, Check };

  constructor(public filter: FilterService, private api: ApiService, private host: ElementRef) {}

  async ngOnInit() {
    if (!this.filter.accounts().length)
      this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
  }

  /** Close the accounts popover when clicking outside the toolbar. */
  @HostListener('document:click', ['$event'])
  onDocClick(e: MouseEvent) {
    if (this.open() && !this.host.nativeElement.contains(e.target)) this.open.set(false);
  }

  isSelected(id: string) { return this.filter.selectedIds().includes(id); }
  toggle(id: string) {
    const ids = this.filter.selectedIds();
    this.filter.selectedIds.set(ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]);
    this.changed.emit();
  }
  /** The "EA" select is an account picker by name; it reflects a single selected account. */
  selectedAccountId(): string | null {
    const ids = this.filter.selectedIds();
    return ids.length === 1 ? ids[0] : null;
  }
  selectByName(id: string | null) {
    this.filter.selectedIds.set(id ? [id] : []);
    this.changed.emit();
  }
  label() {
    const n = this.filter.selectedIds().length;
    return n === 0 ? 'All accounts' : `${n} selected`;
  }
}
