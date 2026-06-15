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
  logout() { localStorage.removeItem('ph_token'); this.token.set(null); }

  /** Whether the signed-in user is an admin, decoded from the JWT `isAdmin` claim. */
  get isAdmin(): boolean {
    try {
      const t = this.token();
      if (!t) return false;
      const payload = JSON.parse(atob(t.split('.')[1] || ''));
      return payload.isAdmin === 'true' || payload.isAdmin === true;
    } catch {
      return false;
    }
  }

  /** The signed-in user's email from the JWT `email` claim ('' if not signed in). */
  get email(): string {
    try {
      const t = this.token();
      if (!t) return '';
      const payload = JSON.parse(atob(t.split('.')[1] || ''));
      return payload.email || payload.sub || '';
    } catch {
      return '';
    }
  }

  /** Store a replacement JWT (e.g. after a timezone change re-issues a token). */
  setToken(token: string) {
    localStorage.setItem('ph_token', token);
    this.token.set(token);
  }

  /** The reporting timezone from the JWT `tz` claim (defaults to Asia/Bangkok). */
  get timeZone(): string {
    try {
      const t = this.token();
      if (!t) return 'Asia/Bangkok';
      const payload = JSON.parse(atob(t.split('.')[1] || ''));
      return payload.tz || 'Asia/Bangkok';
    } catch {
      return 'Asia/Bangkok';
    }
  }

  getMe() {
    return firstValueFrom(this.api.get<{ email: string; timeZone: string }>('/api/me'));
  }

  async setTimeZone(timeZone: string): Promise<void> {
    const res = await firstValueFrom(
      this.api.put<{ token: string }>('/api/me/timezone', { timeZone }),
    );
    this.setToken(res.token);
  }
}
