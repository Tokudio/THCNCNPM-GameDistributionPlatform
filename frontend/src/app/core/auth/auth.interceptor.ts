import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { API_BASE, AUTH_API, REQUIRES_AUTH } from './auth-api';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.context.get(REQUIRES_AUTH) || !request.url.startsWith(`${API_BASE}/`)) {
    return next(request);
  }

  const path = new URL(request.url, 'https://same-origin.invalid').pathname
    .toLowerCase()
    .replace(/\/+$/, '');
  if (!path.startsWith(`${API_BASE}/`) || path === AUTH_API.login || path === AUTH_API.register) {
    return next(request);
  }

  const auth = inject(AuthService);
  const accessToken = auth.getAccessToken();
  if (!accessToken) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })).pipe(
    tap({
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          auth.rejectToken(accessToken);
        }
      },
    }),
  );
};
