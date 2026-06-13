import { Injectable, computed, signal } from '@angular/core';

export interface AccountInfo {
  id: string; accountNumber: number; name: string; broker: string;
  currency: string; ingestKey: string; lastIngestAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class FilterService {
  accounts = signal<AccountInfo[]>([]);
  selectedIds = signal<string[]>([]);          // empty = all accounts
  magic = signal<number | null>(null);          // EA filter
  from = signal<string | null>(null);           // ISO date
  to = signal<string | null>(null);

  queryParams = computed(() => {
    const p: Record<string, string> = {};
    if (this.selectedIds().length) p['accountIds'] = this.selectedIds().join(',');
    if (this.magic() !== null) p['magic'] = String(this.magic());
    if (this.from()) p['from'] = this.from()!;
    if (this.to()) p['to'] = this.to()!;
    return p;
  });
}
