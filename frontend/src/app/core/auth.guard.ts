import { inject } from '@angular/core';
import { Router } from '@angular/router';

export const authGuard = () => {
  if (localStorage.getItem('ph_token')) return true;
  return inject(Router).createUrlTree(['/login']);
};
