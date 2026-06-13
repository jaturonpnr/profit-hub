import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { FilterService, AccountInfo } from '../../core/filter.service';

@Component({
  selector: 'ph-accounts',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <h1>Accounts</h1>
    <table>
      <tr><th>Number</th><th>Name</th><th>Broker</th><th>Ingest Key</th><th>Last data</th><th></th></tr>
      @for (a of filter.accounts(); track a.id) {
        <tr>
          <td>{{ a.accountNumber }}</td><td>{{ a.name }}</td><td>{{ a.broker }}</td>
          <td><code (click)="copy(a.ingestKey)" title="Click to copy">{{ a.ingestKey.slice(0, 8) }}…</code></td>
          <td [class.stale]="isStale(a)">{{ a.lastIngestAtUtc ? (a.lastIngestAtUtc + 'Z' | date:'short') : 'never' }}</td>
          <td><button (click)="remove(a)">Delete</button></td>
        </tr>
      }
    </table>
    <h2>Add account</h2>
    <input [(ngModel)]="num" placeholder="MT5 account number" type="number" />
    <input [(ngModel)]="name" placeholder="Name" />
    <input [(ngModel)]="broker" placeholder="Broker" />
    <button (click)="add()">Add</button>
    @if (copied()) { <span>Copied!</span> }
  `,
  styles: ['.stale{color:#e5484d}'],
})
export class AccountsComponent implements OnInit {
  num = 0; name = ''; broker = '';
  copied = signal(false);
  constructor(private api: ApiService, public filter: FilterService) {}

  async ngOnInit() { await this.reload(); }
  async reload() {
    this.filter.accounts.set(await firstValueFrom(this.api.get<AccountInfo[]>('/api/accounts')));
  }
  async add() {
    await firstValueFrom(this.api.post('/api/accounts', { accountNumber: this.num, name: this.name, broker: this.broker }));
    this.num = 0; this.name = ''; this.broker = '';
    await this.reload();
  }
  async remove(a: AccountInfo) {
    if (!confirm(`Delete account ${a.accountNumber} and all its trades?`)) return;
    await firstValueFrom(this.api.delete(`/api/accounts/${a.id}`));
    await this.reload();
  }
  copy(key: string) { navigator.clipboard.writeText(key); this.copied.set(true); setTimeout(() => this.copied.set(false), 1500); }
  isStale(a: AccountInfo) { return !a.lastIngestAtUtc || Date.now() - Date.parse(a.lastIngestAtUtc + 'Z') > 15 * 60_000; }
}
