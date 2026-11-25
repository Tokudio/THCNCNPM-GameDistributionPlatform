import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';
import { AdminService } from '../../../../core/services/admin.service';
import { GameArtwork } from '../../../store/components/game-artwork/game-artwork';
import { formatMoney } from '../../../store/store-presentation';
import { loadAdminData } from '../../admin-data';
import { orderStatusLabel, orderStatusTone, paymentMethodLabel, paymentStatusLabel, paymentStatusTone } from '../../admin-presentation';

@Component({
  imports: [DatePipe, RouterLink, GameArtwork],
  templateUrl: './order-detail.html',
  styleUrl: './order-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetail {
  private readonly api = inject(AdminService);
  private readonly id = inject(ActivatedRoute).paramMap.pipe(map(params => params.get('id') ?? ''), distinctUntilChanged());
  protected readonly detail = loadAdminData(this.id, id => this.api.order(id), 'Không tìm thấy đơn hàng.');
  protected readonly money = formatMoney;
  protected readonly orderLabel = orderStatusLabel;
  protected readonly orderTone = orderStatusTone;
  protected readonly paymentLabel = paymentStatusLabel;
  protected readonly paymentTone = paymentStatusTone;
  protected readonly methodLabel = paymentMethodLabel;
}
