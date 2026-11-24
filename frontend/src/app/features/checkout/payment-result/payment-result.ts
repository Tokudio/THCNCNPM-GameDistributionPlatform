import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Order, OrderStatus, PaymentStatus } from '../../../core/models/order.models';
import { formatMoney, minorUnits } from '../../store/store-presentation';
import { loadCheckoutOrder } from '../checkout-state';

@Component({
  imports: [RouterLink],
  templateUrl: './payment-result.html',
  styleUrl: './payment-result.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentResult {
  protected readonly checkout = loadCheckoutOrder();
  protected readonly money = formatMoney;

  protected outcome(order: Order): 'paid' | 'free' | 'failed' | 'pending' | 'unknown' {
    if (order.status === OrderStatus.Paid && order.ownershipGranted) {
      if (order.paymentStatus === PaymentStatus.Succeeded) return 'paid';
      if (order.paymentStatus === null && minorUnits(order.totalAmount) === 0n) return 'free';
    }
    if (order.status === OrderStatus.Cancelled && order.paymentStatus === PaymentStatus.Failed) return 'failed';
    return order.status === OrderStatus.Pending ? 'pending' : 'unknown';
  }
}
