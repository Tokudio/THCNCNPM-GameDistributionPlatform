import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, Subject, switchMap } from 'rxjs';
import { Order } from '../../core/models/order.models';
import { OrderService } from '../../core/services/order.service';

type OrderState = { kind: 'loading' } | { kind: 'ready'; order: Order } | { kind: 'error'; message: string };

export function checkoutError(error: unknown): { message: string; owned: boolean } {
  const fallback = { message: 'We could not load or update this order. Try again.', owned: false };
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.error?.name === 'TimeoutError') return { message: 'The request timed out. Check the order status before trying again.', owned: false };
  if (error.status === 0) return { message: 'Check your connection. If you submitted a payment, check the order status before trying again.', owned: false };
  if (error.error?.code === 'game_already_owned') return { message: 'You already own this game.', owned: true };
  if (error.error?.code === 'checkout_unavailable') return { message: 'Mock payment is unavailable in this environment.', owned: false };
  if (error.status === 401) return { message: 'Sign in again to continue.', owned: false };
  if (error.status === 403) return { message: 'You do not have permission to access this order.', owned: false };
  if (error.status === 404) return { message: 'The game or order is unavailable.', owned: false };
  if (error.status === 409) return { message: error.error?.detail || 'The order changed. Reload its current status.', owned: false };
  return fallback;
}

export function loadCheckoutOrder() {
  const route = inject(ActivatedRoute);
  const orders = inject(OrderService);
  const reload = new Subject<void>();
  const state = toSignal(combineLatest([
    route.paramMap.pipe(map(params => params.get('id') ?? ''), distinctUntilChanged()),
    reload.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([id]) => orders.get(id).pipe(
      map(order => ({ kind: 'ready', order }) as const),
      catchError(error => of<OrderState>({ kind: 'error', message: checkoutError(error).message })),
      startWith<OrderState>({ kind: 'loading' }),
    )),
  ), { requireSync: true });
  return { state, retry: () => reload.next() };
}
