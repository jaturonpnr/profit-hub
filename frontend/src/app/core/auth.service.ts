import { Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  token = signal<string | null>(localStorage.getItem('ph_token'));
  constructor(private api: ApiService) {}
  async login(email: string, password: string) {
    const res = await firstValueFrom(this.api.post<{ token: string }>('/api/auth/login', { email, password }));
    localStorage.setItem('ph_token', res.token);
    this.token.set(res.token);
  }
  async register(email: string, password: string) {
    await firstValueFrom(this.api.post('/api/auth/register', { email, password }));
    await this.login(email, password);
  }
  logout() { localStorage.removeItem('ph_token'); this.token.set(null); }
}
