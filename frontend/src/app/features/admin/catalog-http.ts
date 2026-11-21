import { HttpContext, HttpErrorResponse } from '@angular/common/http';
import { REQUIRES_AUTH } from '../../core/auth/auth-api';

export const adminContext = () => new HttpContext().set(REQUIRES_AUTH, true);

export function catalogError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Your session ended. Sign in again.';
    if (error.status === 403) return 'The Admin role is required for this operation.';
    if (error.status === 404) return 'This item could not be found. It may have been removed.';
    if (error.status === 400 || error.status === 409) {
      const body = error.error as { detail?: unknown; errors?: Record<string, unknown> } | null;
      const fields = Object.values(body?.errors ?? {}).flat()
        .filter((message): message is string => typeof message === 'string');
      if (fields.length) return [...new Set(fields)].join(' ');
      if (typeof body?.detail === 'string') return body.detail;
      return error.status === 409 ? 'This change conflicts with existing data.' : 'Check the form and try again.';
    }
  }
  return 'The catalog could not be reached. Try again.';
}
