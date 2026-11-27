import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, filter, finalize, map, of, startWith, Subject, switchMap, takeUntil } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { GameStatus } from '../../../core/models/game-store.models';
import { LibraryDownloadInfo, LibraryGameDetail as GameDetail } from '../../../core/models/user-library.models';
import { isLibraryGameNotFound, UserLibraryService } from '../../../core/services/user-library.service';
import { GameArtwork } from '../../store/components/game-artwork/game-artwork';
import { storeError } from '../../store/store-presentation';
import { acquiredTimestamp } from '../library-date';

type Result = { kind: 'loading' | 'missing' } | { kind: 'ready'; game: GameDetail; userId: string | undefined } |
  { kind: 'error'; message: string };
type DownloadTarget = { userId: string; gameId: string };
type PackageState = {
  target: DownloadTarget;
  kind: 'loading' | 'ready' | 'error';
  info?: LibraryDownloadInfo;
};

function sameTarget(a: DownloadTarget | null, b: DownloadTarget | null): boolean {
  return a?.userId === b?.userId && a?.gameId === b?.gameId;
}

@Component({
  imports: [DatePipe, RouterLink, GameArtwork],
  templateUrl: './library-game-detail.html',
  styleUrl: './library-game-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryGameDetail {
  protected readonly auth = inject(AuthService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly library = inject(UserLibraryService);
  private readonly refresh = new Subject<void>();
  protected readonly published = GameStatus.Published;
  protected readonly acquiredTimestamp = acquiredTimestamp;
  protected readonly result = toSignal(combineLatest([
    inject(ActivatedRoute).paramMap.pipe(map(params => params.get('gameId') ?? ''), distinctUntilChanged()),
    this.refresh.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([id]) => {
      const userId = this.auth.currentUser()?.id;
      return this.library.get(id).pipe(
        map(game => ({ kind: 'ready', game, userId }) as const),
        catchError(error => of<Result>(isLibraryGameNotFound(error)
          ? { kind: 'missing' } : { kind: 'error', message: storeError(error) })),
        startWith<Result>({ kind: 'loading' }),
      );
    }),
  ), { requireSync: true });

  private readonly target = computed(() => {
    const view = this.result();
    const userId = this.auth.currentUser()?.id;
    return view.kind === 'ready' && userId && view.userId === userId ? { userId, gameId: view.game.gameId } : null;
  });
  private readonly targets = toObservable(this.target).pipe(distinctUntilChanged(sameTarget));
  private readonly retryPackage = new Subject<void>();
  private readonly packageState = toSignal(combineLatest([this.targets, this.retryPackage.pipe(startWith(undefined))]).pipe(
    switchMap(([target]) => target ? this.library.downloadInfo(target.gameId).pipe(
      map((info): PackageState => info.gameId === target.gameId && info.isOwned && (!info.isDownloadAvailable || info.fileName)
        ? { target, kind: 'ready', info } : { target, kind: 'error' }),
      catchError(() => of<PackageState>({ target, kind: 'error' })),
      startWith<PackageState>({ target, kind: 'loading' }),
    ) : of(null)),
  ), { initialValue: null });
  protected readonly packageStatus = computed(() => {
    const state = this.packageState();
    return state && sameTarget(state.target, this.target()) ? state : null;
  });
  protected readonly downloading = signal(false);
  private readonly failure = signal<{ target: DownloadTarget; message: string } | null>(null);
  protected readonly downloadError = computed(() => {
    const failure = this.failure();
    return failure && sameTarget(failure.target, this.target()) ? failure.message : null;
  });

  protected retry(): void { this.refresh.next(); }

  protected retryDownloadInfo(): void { this.retryPackage.next(); }

  protected download(): void {
    const target = this.target();
    const info = this.packageStatus()?.info;
    if (!target || !info?.isOwned || !info.isDownloadAvailable || info.gameId !== target.gameId ||
        !info.fileName || this.downloading()) return;

    const fileName = info.fileName;
    this.failure.set(null);
    this.downloading.set(true);
    this.library.download(target.gameId).pipe(
      takeUntil(this.targets.pipe(filter(current => !sameTarget(current, target)))),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.downloading.set(false)),
    ).subscribe({
      next: blob => {
        if (!sameTarget(this.target(), target)) return;
        const url = URL.createObjectURL(blob);
        const link = this.document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
      },
      error: (error: unknown) => {
        if (!sameTarget(this.target(), target)) return;
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.retryDownloadInfo();
        } else {
          this.failure.set({ target, message: 'Không tải được tệp. Vui lòng thử lại.' });
        }
      },
    });
  }
}
