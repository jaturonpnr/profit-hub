import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api.service';
import { FilterService } from '../../core/filter.service';
import { FilterBarComponent } from '../../shared/filter-bar.component';

interface Trade {
  symbol: string; direction: string; lots: number; openPrice: number; closePrice: number;
  closeTimeUtc: string; netProfit: number; magicNumber: number;
}

@Component({
  selector: 'ph-trades',
  standalone: true,
  imports: [FilterBarComponent, FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1>Trades</h1>
    <ph-filter-bar (changed)="load(1)" />
    <div class="actions">
      <button (click)="exportCsv('trades.csv', {})">Export trades CSV</button>
      <select [(ngModel)]="summaryPeriod">
        <option value="day">Daily</option><option value="week">Weekly</option><option value="month">Monthly</option>
      </select>
      <button (click)="exportCsv('summary.csv', { period: summaryPeriod })">Export summary CSV</button>
    </div>
    <table>
      <tr><th>Symbol</th><th>Type</th><th>Lots</th><th>Open</th><th>Close</th><th>Profit</th><th>EA</th><th>Closed</th></tr>
      @for (t of trades(); track $index) {
        <tr>
          <td>{{ t.symbol }}</td>
          <td [class.buy]="t.direction === 'buy'" [class.sell]="t.direction === 'sell'">{{ t.direction.toUpperCase() }}</td>
          <td>{{ t.lots }}</td><td>{{ t.openPrice }}</td><td>{{ t.closePrice }}</td>
          <td [class.neg]="t.netProfit < 0" class="profit">{{ t.netProfit | number:'1.2-2' }}</td>
          <td>{{ t.magicNumber }}</td>
          <td>{{ t.closeTimeUtc + 'Z' | date:'short' }}</td>
        </tr>
      }
    </table>
    <div class="pager">
      <button [disabled]="page() <= 1" (click)="load(page() - 1)">Prev</button>
      <span>{{ page() }} / {{ pages() }}</span>
      <button [disabled]="page() >= pages()" (click)="load(page() + 1)">Next</button>
    </div>
  `,
  styles: [`.buy{color:#30a46c}.sell{color:#e5484d}.profit{color:#30a46c}.neg{color:#e5484d!important}
            .actions{display:flex;gap:.5rem;margin-bottom:1rem}
            .pager{display:flex;gap:1rem;margin-top:1rem;align-items:center}`],
})
export class TradesComponent implements OnInit {
  trades = signal<Trade[]>([]);
  page = signal(1); total = signal(0);
  summaryPeriod = 'day';
  constructor(private api: ApiService, private filter: FilterService, private http: HttpClient) {}

  pages() { return Math.max(1, Math.ceil(this.total() / 50)); }
  async ngOnInit() { await this.load(1); }
  async load(page: number) {
    this.page.set(page);
    const res = await firstValueFrom(this.api.get<{ total: number; items: Trade[] }>(
      '/api/trades', { ...this.filter.queryParams(), page: String(page) }));
    this.trades.set(res.items); this.total.set(res.total);
  }
  async exportCsv(file: string, extra: Record<string, string>) {
    // The auth interceptor is registered globally, so this HttpClient.get is sent with the
    // Bearer token even though we pass the full absolute URL (the CSV endpoints require auth,
    // and a token cannot be attached to a plain <a href> download).
    const params = new URLSearchParams({ ...this.filter.queryParams(), ...extra });
    const blob = await firstValueFrom(this.http.get(
      `${environment.apiUrl}/api/export/${file}?${params}`, { responseType: 'blob' }));
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob); a.download = file; a.click();
    URL.revokeObjectURL(a.href);
  }
}
