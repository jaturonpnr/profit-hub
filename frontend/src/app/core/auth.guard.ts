import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard = () => {
  if (localStorage.getItem('ph_token')) return true;
  return inject(Router).createUrlTree(['/login']);
};

/** Allows admins only; non-admins are redirected to the dashboard. */
export const adminGuard = () => {
  if (inject(AuthService).isAdmin) return true;
  return inject(Router).createUrlTree(['/']);
};
