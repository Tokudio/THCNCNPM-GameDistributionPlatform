import { HttpClient, HttpContext, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE, REQUIRES_AUTH } from '../auth/auth-api';
import { Page } from '../models/game-store.models';
import { LibraryGame, LibraryGameDetail } from '../models/user-library.models';

export function isLibraryGameNotFound(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404 && error.error?.code === 'library_game_not_found';
}

@Injectable({ providedIn: 'root' })
export class UserLibraryService {
  private readonly http = inject(HttpClient);
  private readonly url = `${API_BASE}/library/my`;

  list(search: string, page: number) {
    let params = new HttpParams().set('pageNumber', page).set('pageSize', 12);
    if (search) params = params.set('search', search);
    return this.http.get<Page<LibraryGame>>(this.url, { ...this.options(), params });
  }

  get(gameId: string) {
    return this.http.get<LibraryGameDetail>(`${this.url}/${encodeURIComponent(gameId)}`, this.options());
  }

  private options() {
    return { context: new HttpContext().set(REQUIRES_AUTH, true), timeout: 15_000 };
  }
}
