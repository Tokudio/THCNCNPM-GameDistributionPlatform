import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { GameStatus } from '../../../core/models/game-store.models';
import { LibraryGameDetail as GameDetail } from '../../../core/models/user-library.models';
import { isLibraryGameNotFound, UserLibraryService } from '../../../core/services/user-library.service';
import { GameArtwork } from '../../store/components/game-artwork/game-artwork';
import { storeError } from '../../store/store-presentation';
import { acquiredTimestamp } from '../library-date';

type Result = { kind: 'loading' | 'missing' } | { kind: 'ready'; game: GameDetail } | { kind: 'error'; message: string };

@Component({
  imports: [DatePipe, RouterLink, GameArtwork],
  templateUrl: './library-game-detail.html',
  styleUrl: './library-game-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryGameDetail {
  protected readonly auth = inject(AuthService);
  private readonly library = inject(UserLibraryService);
  private readonly refresh = new Subject<void>();
  protected readonly published = GameStatus.Published;
  protected readonly acquiredTimestamp = acquiredTimestamp;
  protected readonly result = toSignal(combineLatest([
    inject(ActivatedRoute).paramMap.pipe(map(params => params.get('gameId') ?? ''), distinctUntilChanged()),
    this.refresh.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([id]) => this.library.get(id).pipe(
      map(game => ({ kind: 'ready', game }) as const),
      catchError(error => of<Result>(isLibraryGameNotFound(error)
        ? { kind: 'missing' } : { kind: 'error', message: storeError(error) })),
      startWith<Result>({ kind: 'loading' }),
    )),
  ), { requireSync: true });

  protected retry(): void { this.refresh.next(); }
}
