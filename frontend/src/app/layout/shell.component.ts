import { Component } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'ph-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <nav>
        <h2>Profit Hub</h2>
        <a routerLink="/" [routerLinkActiveOptions]="{exact:true}" routerLinkActive="active">Dashboard</a>
        <a routerLink="/trades" routerLinkActive="active">Trades</a>
        <a routerLink="/accounts" routerLinkActive="active">Accounts</a>
        <button (click)="logout()">Sign out</button>
      </nav>
      <main><router-outlet /></main>
    </div>
  `,
  styles: [`.shell{display:grid;grid-template-columns:200px 1fr;min-height:100vh}
            nav{display:flex;flex-direction:column;gap:.5rem;padding:1rem;border-right:1px solid #2a2f3a}
            nav a.active{color:#30a46c} main{padding:1.5rem}`],
})
export class ShellComponent {
  constructor(private auth: AuthService, private router: Router) {}
  logout() { this.auth.logout(); this.router.navigate(['/login']); }
}
