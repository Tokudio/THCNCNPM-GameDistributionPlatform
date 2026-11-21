import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE } from '../../core/auth/auth-api';
import { adminContext } from './catalog-http';
import { GameDetails, GameFilters, GameInput, GameStatus, GameSummary, Page } from './catalog.models';

const gamesUrl = `${API_BASE}/admin/games`;

@Injectable({ providedIn: 'root' })
export class AdminGameService {
  private readonly http = inject(HttpClient);

  list(filters: GameFilters, pageNumber: number) {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', 12);
    for (const [key, value] of Object.entries(filters)) {
      if (value !== null && value !== '') params = params.set(key, value);
    }
    return this.http.get<Page<GameSummary>>(gamesUrl, { params, context: adminContext() });
  }

  get(id: string) {
    return this.http.get<GameDetails>(`${gamesUrl}/${id}`, { context: adminContext() });
  }

  create(input: GameInput) {
    return this.http.post<GameDetails>(gamesUrl, input, { context: adminContext() });
  }

  update(id: string, input: GameInput) {
    return this.http.put<GameDetails>(`${gamesUrl}/${id}`, input, { context: adminContext() });
  }

  updateStatus(id: string, status: GameStatus) {
    return this.http.patch<GameDetails>(`${gamesUrl}/${id}/status`, { status }, { context: adminContext() });
  }
}
