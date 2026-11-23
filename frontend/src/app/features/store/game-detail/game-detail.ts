import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';
import { MediaType, StoreGameDetails } from '../../../core/models/game-store.models';
import { GameStoreService } from '../../../core/services/game-store.service';
import { GameArtwork } from '../components/game-artwork/game-artwork';
import { GameGallery } from '../components/game-gallery/game-gallery';
import { gamePrice, publicWebUrl, storeError } from '../store-presentation';

type DetailState = { kind: 'loading' | 'missing' } | { kind: 'ready'; game: StoreGameDetails } | { kind: 'error'; message: string };

@Component({
  imports: [DatePipe, RouterLink, GameArtwork, GameGallery],
  templateUrl: './game-detail.html',
  styleUrl: './game-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameDetail {
  private readonly api = inject(GameStoreService);
  private readonly refresh = new Subject<void>();
  protected readonly price = gamePrice;
  protected readonly state = toSignal(combineLatest([
    inject(ActivatedRoute).paramMap.pipe(map(params => params.get('id') ?? ''), distinctUntilChanged()),
    this.refresh.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([id]) => this.api.get(id).pipe(
      map(game => ({ kind: 'ready', game }) as const),
      catchError((error: unknown) => of<DetailState>(
        error instanceof HttpErrorResponse && error.status === 404
          ? { kind: 'missing' } : { kind: 'error', message: storeError(error) },
      )),
      startWith<DetailState>({ kind: 'loading' }),
    )),
  ), { requireSync: true });

  protected excerpt(description: string): string {
    const first = description.trim().split(/\r?\n/)[0];
    return first.length <= 160 ? first : first.slice(0, 157).trimEnd() + '…';
  }

  protected videoLinks(game: StoreGameDetails): { url: string; label: string }[] {
    const trailer = publicWebUrl(game.trailerUrl);
    const urls = game.media.filter(item => item.type === MediaType.Video)
      .sort((a, b) => a.displayOrder - b.displayOrder).map(item => publicWebUrl(item.mediaUrl));
    return [...new Set([trailer, ...urls].filter((url): url is string => url !== null))]
      .map((url, index) => ({ url, label: url === trailer ? 'Watch trailer' : `Watch video ${trailer ? index : index + 1}` }));
  }

  protected retry(): void { this.refresh.next(); }
}
