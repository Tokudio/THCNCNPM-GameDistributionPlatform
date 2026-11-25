import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, finalize, map, of, startWith, Subject, switchMap } from 'rxjs';
import { loginUrl } from '../../../core/auth/auth.guards';
import { AuthService } from '../../../core/auth/auth.service';
import { MediaType, StoreGameDetails } from '../../../core/models/game-store.models';
import { OrderStatus } from '../../../core/models/order.models';
import { GameStoreService } from '../../../core/services/game-store.service';
import { OrderService } from '../../../core/services/order.service';
import { isLibraryGameNotFound, UserLibraryService } from '../../../core/services/user-library.service';
import { checkoutError } from '../../checkout/checkout-state';
import { GameArtwork } from '../components/game-artwork/game-artwork';
import { GameGallery } from '../components/game-gallery/game-gallery';
import { gamePrice, publicWebUrl, storeError } from '../store-presentation';

type DetailState = { kind: 'loading' | 'missing' } | { kind: 'ready'; game: StoreGameDetails } | { kind: 'error'; message: string };
type Ownership = 'checking' | 'owned' | 'available' | 'unavailable' | 'sign-in-required';

@Component({
  imports: [DatePipe, RouterLink, GameArtwork, GameGallery],
  templateUrl: './game-detail.html',
  styleUrl: './game-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameDetail {
  private readonly api = inject(GameStoreService);
  private readonly orders = inject(OrderService);
  private readonly library = inject(UserLibraryService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  private readonly refreshOwnership = new Subject<void>();
  private readonly purchaseFailure = signal<{ gameId: string; userId: string; message: string; owned: boolean } | null>(null);
  protected readonly purchasing = signal(false);
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

  private readonly ownershipTarget = computed(() => {
    const userId = this.auth.currentUser()?.id;
    const detail = this.state();
    return userId && detail.kind === 'ready' ? { userId, gameId: detail.game.id } : null;
  });
  private readonly ownershipResult = toSignal(combineLatest([
    toObservable(this.ownershipTarget).pipe(distinctUntilChanged((a, b) => a?.userId === b?.userId && a?.gameId === b?.gameId)),
    this.refreshOwnership.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([target]) => target ? this.library.get(target.gameId).pipe(
      map(game => ({ ...target, status: (game.isOwned === true && game.gameId === target.gameId ? 'owned' : 'unavailable') as Ownership })),
      catchError((error: unknown) => of({ ...target,
        status: (isLibraryGameNotFound(error) ? 'available' : 'unavailable') as Ownership })),
      startWith({ ...target, status: 'checking' as const }),
    ) : of(null)),
  ), { initialValue: null });
  protected readonly ownership = computed<Ownership>(() => {
    const target = this.ownershipTarget();
    if (!target) return this.auth.sessionNotice() ? 'sign-in-required' : 'available';
    const result = this.ownershipResult();
    if (result?.userId !== target.userId || result.gameId !== target.gameId) return 'checking';
    return result.status === 'available' && this.checkoutFeedback(target.gameId)?.owned ? 'unavailable' : result.status;
  });

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

  protected retryOwnership(): void { this.refreshOwnership.next(); }

  protected signInLink(gameId: string) {
    const destination = this.router.createUrlTree(['/store/games', gameId]).toString();
    return loginUrl(this.router, destination);
  }

  protected checkoutFeedback(gameId: string) {
    const failure = this.purchaseFailure();
    return failure?.gameId === gameId && failure.userId === this.auth.currentUser()?.id ? failure : null;
  }

  protected buy(game: StoreGameDetails): void {
    if (!game.mockPaymentAvailable || this.purchasing() || this.ownership() !== 'available') return;
    const user = this.auth.currentUser();
    if (!user || !this.auth.hasVerifiedSession()) {
      void this.router.navigateByUrl(this.signInLink(game.id));
      return;
    }

    this.purchasing.set(true);
    this.purchaseFailure.set(null);
    const origin = this.router.url;
    this.orders.create(game.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.purchasing.set(false)),
    ).subscribe({
      next: order => {
        if (this.router.url !== origin || this.auth.currentUser()?.id !== user.id) return;
        void this.router.navigate(order.status === OrderStatus.Paid
          ? ['/checkout', order.id, 'result'] : ['/checkout', order.id]);
      },
      error: error => {
        const target = this.ownershipTarget();
        if (target?.userId !== user.id || target.gameId !== game.id) return;
        const feedback = checkoutError(error);
        this.purchaseFailure.set({ gameId: game.id, userId: user.id, ...feedback });
        if (feedback.owned) this.retryOwnership();
      },
    });
  }
}
