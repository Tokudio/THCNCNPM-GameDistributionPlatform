import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE, REQUIRES_AUTH } from '../auth/auth-api';
import { MockPaymentOutcome, MockPaymentResult, Order } from '../models/order.models';

@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly http = inject(HttpClient);
  private readonly url = `${API_BASE}/orders`;

  create(gameId: string) {
    return this.http.post<Order>(this.url, { gameId }, this.options());
  }

  get(orderId: string) {
    return this.http.get<Order>(`${this.url}/${encodeURIComponent(orderId)}`, this.options());
  }

  confirm(orderId: string, outcome: MockPaymentOutcome) {
    return this.http.post<MockPaymentResult>(`${this.url}/${encodeURIComponent(orderId)}/mock-payment`,
      { outcome }, this.options());
  }

  private options() {
    return { context: new HttpContext().set(REQUIRES_AUTH, true), timeout: 15_000 };
  }
}
