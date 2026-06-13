import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'ph-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="login-card">
      <h1>Profit Hub</h1>
      <input [(ngModel)]="email" placeholder="Email" type="email" />
      <input [(ngModel)]="password" placeholder="Password" type="password" />
      @if (error()) { <p class="error">{{ error() }}</p> }
      <button (click)="submit(false)">Login</button>
      <button class="secondary" (click)="submit(true)">Register</button>
    </div>
  `,
  styles: [`.login-card{max-width:320px;margin:15vh auto;display:flex;flex-direction:column;gap:.75rem}
            .error{color:#e5484d;margin:0}`],
})
export class LoginComponent {
  email = ''; password = '';
  error = signal('');
  constructor(private auth: AuthService, private router: Router) {}
  async submit(register: boolean) {
    try {
      register ? await this.auth.register(this.email, this.password)
               : await this.auth.login(this.email, this.password);
      this.router.navigate(['/']);
    } catch { this.error.set(register ? 'Registration failed' : 'Wrong email or password'); }
  }
}
