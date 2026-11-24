import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { MockPaymentOutcome, OrderStatus } from '../../../core/models/order.models';
import { OrderService } from '../../../core/services/order.service';
import { GameArtwork } from '../../store/components/game-artwork/game-artwork';
import { formatMoney, minorUnits } from '../../store/store-presentation';
import { checkoutError, loadCheckoutOrder } from '../checkout-state';

@Component({
  imports: [RouterLink, GameArtwork],
  templateUrl: './order-checkout.html',
  styleUrl: './order-checkout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderCheckout {
  private readonly orders = inject(OrderService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly confirm = toSignal(inject(ActivatedRoute).data.pipe(map(data => data['confirm'] === true)),
    { initialValue: false });
  protected readonly checkout = loadCheckoutOrder();
  protected readonly status = OrderStatus;
  protected readonly outcome = MockPaymentOutcome;
  protected readonly money = formatMoney;
  protected readonly minorUnits = minorUnits;
  protected readonly processing = signal(false);
  protected readonly failure = signal<{ id: string; message: string; needsStatusCheck: boolean } | null>(null);

  protected statusCheckRequired(id: string): boolean {
    const failure = this.failure();
    return failure?.id === id && failure.needsStatusCheck;
  }

  protected pay(id: string, outcome: MockPaymentOutcome): void {
    const state = this.checkout.state();
    if (!this.confirm() || this.processing() || this.statusCheckRequired(id) || state.kind !== 'ready' || state.order.id !== id ||
        state.order.status !== OrderStatus.Pending || !state.order.mockPaymentAvailable) return;

    this.processing.set(true);
    this.failure.set(null);
    const origin = this.router.url;
    this.orders.confirm(id, outcome).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.processing.set(false)),
    ).subscribe({
      next: () => {
        if (this.router.url === origin) void this.router.navigate(['/checkout', id, 'result']);
      },
      error: (error: unknown) => this.failure.set({
        id,
        message: checkoutError(error).message,
        needsStatusCheck: error instanceof HttpErrorResponse && (error.status < 400 || error.status >= 500),
      }),
    });
  }
}
