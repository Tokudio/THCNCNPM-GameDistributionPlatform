import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, from, map, mergeMap, of, throwError, toArray } from 'rxjs';
import { API_BASE } from '../auth/auth-api';
import { Category, GameStatus, Page, StoreGame, StoreGameDetails, StoreQuery } from '../models/game-store.models';

// Preserve SQL decimal prices before JavaScript parses JSON numbers. Match entire string
// tokens so a quoted "price" in a title or description is left alone.
function readGameJson<T>(body: string): T {
  return JSON.parse(body.replace(/"(?:\\.|[^"\\])*"(?:\s*:\s*\d+(?:\.\d+)?(?=\s*[,}]))?/g, token => {
    const price = /^"price"\s*:\s*(\d+(?:\.\d+)?)$/.exec(token);
    return price ? `"price":"${price[1]}"` : token;
  })) as T;
}

@Injectable({ providedIn: 'root' })
export class GameStoreService {
  private readonly http = inject(HttpClient);

  list(query: StoreQuery = {}) {
    let params = new HttpParams()
      .set('pageNumber', query.page ?? 1)
      .set('pageSize', Math.max(1, Math.min(50, query.pageSize ?? 6)));
    if (query.search) params = params.set('search', query.search);
    if (query.category) params = params.set('categoryId', query.category);
    if (query.minPrice) params = params.set('minPrice', query.minPrice);
    if (query.maxPrice) params = params.set('maxPrice', query.maxPrice);
    if (query.sort !== undefined) params = params.set('sort', query.sort);

    return this.http.get(`${API_BASE}/games`, { params, responseType: 'text' }).pipe(
      map(body => readGameJson<Page<StoreGame>>(body)),
      map(page => ({ ...page, items: page.items.filter(game => game.status === GameStatus.Published) })),
    );
  }

  get(id: string) {
    return this.http.get(`${API_BASE}/games/${encodeURIComponent(id)}`, { responseType: 'text' }).pipe(
      map(body => readGameJson<StoreGameDetails>(body)),
      mergeMap(game => game.status === GameStatus.Published
        ? of(game)
        : throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' }))),
    );
  }

  categories() {
    return this.http.get<Category[]>(`${API_BASE}/categories`).pipe(
      map(categories => categories.filter(category => category.isActive)),
    );
  }

  detailsFor(games: StoreGame[]) {
    return from(games).pipe(
      mergeMap(game => this.get(game.id).pipe(
        map(detail => [game.id, detail] as const),
        catchError((error: unknown) => of([
          game.id, error instanceof HttpErrorResponse && error.status === 404 ? null : undefined,
        ] as const)),
      ), 4),
      toArray(),
      map(entries => new Map(entries)),
    );
  }
}
