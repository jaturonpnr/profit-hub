import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { FilterService, AccountInfo } from '../core/filter.service';

@Component({
  selector: 'ph-filter-bar',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="bar">
      <details>
        <summary>{{ label() }}</summary>
        @for (a of filter.accounts(); track a.id) {
          <label><input type="checkbox" [checked]="isSelected(a.id)" (change)="toggle(a.id)" />
            {{ a.name || a.accountNumber }}</label>
        }
      </details>
      <select [ngModel]="filter.magic()" (ngModelChange)="setMagic($event)">
        <option [ngValue]="null">All EAs</option>
        @for (ea of eas; track ea.magicNumber) { <option [ngValue]="ea.magicNumber">{{ ea.name }}</option> }
      </select>
      <input type="date" [ngModel]="filter.from()" (ngModelChange)="filter.from.set($event); changed.emit()" />
      <input type="date" [ngModel]="filter.to()" (ngModelChange)="filter.to.set($event); changed.emit()" />
    </div>
  `,
  styles: ['.bar{display:flex;gap:1rem;align-items:center;margin-bottom:1rem}'],
})
export class FilterBarComponent implements OnInit {
  @Output() changed = new EventEmitter<void>();
  eas: { magicNumber: number; name: string }[] = [];
  constructor(public filter: FilterService, private api: ApiService) {}

  async ngOnInit() {
    if (!this.filter.accounts().length)
      this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
    this.eas = await firstValueFrom(this.api.get<{ magicNumber: number; name: string }[]>('/api/summary/by-ea'));
  }
  isSelected(id: string) { return this.filter.selectedIds().includes(id); }
  toggle(id: string) {
    const ids = this.filter.selectedIds();
    this.filter.selectedIds.set(ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]);
    this.changed.emit();
  }
  setMagic(m: number | null) { this.filter.magic.set(m); this.changed.emit(); }
  label() {
    const n = this.filter.selectedIds().length;
    return n === 0 ? 'All accounts' : `${n} account(s)`;
  }
}
