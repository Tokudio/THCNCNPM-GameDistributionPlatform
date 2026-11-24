import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

export function loginUrl(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/login'], { queryParams: { returnUrl } });
}

export function postLoginUrl(router: Router, returnUrl: string | null): UrlTree {
  return returnUrl && /^\/(?:profile|access-denied|store(?:\/games(?:\/[a-fA-F0-9-]+)?)?|checkout\/[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}(?:\/(?:payment|result))?|admin(?:\/categories|\/games(?:\/new|\/[a-fA-F0-9-]+\/edit)?)?)(?:[?#]|$)/.test(returnUrl)
    ? router.parseUrl(returnUrl)
    : router.createUrlTree(['/profile']);
}

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.hasVerifiedSession() ? true : loginUrl(router, state.url);
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.hasVerifiedSession() ? router.createUrlTree(['/profile']) : true;
};

export function roleGuard(role: string): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.hasVerifiedSession()) {
      return loginUrl(router, state.url);
    }

    return auth.getCurrentUser().pipe(
      map(user => user.roles.includes(role) || router.createUrlTree(['/access-denied'])),
      catchError(() => of(auth.hasVerifiedSession()
        ? router.createUrlTree(['/access-denied'], { queryParams: { reason: 'unavailable' } })
        : loginUrl(router, state.url))),
    );
  };
}
