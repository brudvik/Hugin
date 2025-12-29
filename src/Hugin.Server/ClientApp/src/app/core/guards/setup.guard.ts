// Hugin Admin Panel - Setup Guard
import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { map, catchError, of } from 'rxjs';
import { SetupService } from '../services/setup.service';

export const setupGuard: CanActivateFn = (route, state) => {
  const setupService = inject(SetupService);
  const router = inject(Router);

  // If we're already on the setup page, allow it
  if (state.url.startsWith('/setup')) {
    return true;
  }

  return setupService.checkSetupRequired().pipe(
    map(result => {
      if (result.setupRequired) {
        router.navigate(['/setup']);
        return false;
      }
      return true;
    }),
    catchError(() => {
      // If we can't check, assume setup is not required
      return of(true);
    })
  );
};
