import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, map, of, startWith, Subject, switchMap } from 'rxjs';
import { Category, StoreCardGame, StoreGameDetails } from '../../../core/models/game-store.models';
import { GameStoreService } from '../../../core/services/game-store.service';
import { GameArtwork } from '../components/game-artwork/game-artwork';
import { GameCard } from '../components/game-card/game-card';
import { storeError, toCards } from '../store-presentation';

type HomeGames = { hero: (StoreCardGame & Pick<StoreGameDetails, 'bannerImageUrl'>) | null; featured: StoreCardGame[]; recent: StoreCardGame[] };
type Result<T> = { loading: true } | { loading: false; data: T } | { loading: false; error: string };

@Component({
  imports: [RouterLink, GameArtwork, GameCard],
  templateUrl: './store-home.html',
  styleUrl: './store-home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreHome {
  private readonly api = inject(GameStoreService);
  private readonly retryGames = new Subject<void>();
  private readonly retryCategories = new Subject<void>();
  protected readonly games = toSignal(this.retryGames.pipe(
    startWith(undefined),
    switchMap(() => this.api.list({ pageSize: 9 }).pipe(
      switchMap(page => this.api.detailsFor(page.items).pipe(
        map(details => {
          const cards = toCards(page.items, details);
          const first = cards[0];
          return {
            loading: false,
            data: {
              hero: first ? { ...first, bannerImageUrl: details.get(first.id)?.bannerImageUrl ?? null } : null,
              featured: cards.slice(0, 5),
              recent: cards.slice(5, 9),
            },
          } as const;
        }),
      )),
      catchError(error => of<Result<HomeGames>>({ loading: false, error: storeError(error) })),
      startWith<Result<HomeGames>>({ loading: true }),
    )),
  ), { requireSync: true });

  protected readonly categories = toSignal(this.retryCategories.pipe(
    startWith(undefined),
    switchMap(() => this.api.categories().pipe(
      map(data => ({ loading: false, data }) as const),
      catchError(error => of<Result<Category[]>>({ loading: false, error: storeError(error) })),
      startWith<Result<Category[]>>({ loading: true }),
    )),
  ), { requireSync: true });

  protected reloadGames(): void { this.retryGames.next(); }
  protected reloadCategories(): void { this.retryCategories.next(); }
}
