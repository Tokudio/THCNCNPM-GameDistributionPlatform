import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, map, tap } from 'rxjs';
import { AdminService } from '../../../../core/services/admin.service';
import { formatMoney } from '../../../store/store-presentation';
import { adminOption, adminSearch, loadAdminData, readAdminPage } from '../../admin-data';
import { orderStatusLabel, orderStatusTone, paymentStatusLabel, paymentStatusTone } from '../../admin-presentation';
import { AdminPager } from '../../admin-pager/admin-pager';

@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe, AdminPager],
  templateUrl: './order-list.html',
  styleUrl: './order-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderList {
  private readonly api = inject(AdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly money = formatMoney;
  protected readonly orderLabel = orderStatusLabel;
  protected readonly orderTone = orderStatusTone;
  protected readonly paymentLabel = paymentStatusLabel;
  protected readonly paymentTone = paymentStatusTone;
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    status: new FormControl('', { nonNullable: true }),
    paymentStatus: new FormControl('', { nonNullable: true }),
    oldestFirst: new FormControl('', { nonNullable: true }),
  });
  private readonly query = this.route.queryParamMap.pipe(
    map(params => ({ page: readAdminPage(params), filters: {
      search: adminSearch(params.get('search')),
      status: adminOption(params.get('status'), ['0', '1', '2']),
      paymentStatus: adminOption(params.get('paymentStatus'), ['0', '1', '2']),
      oldestFirst: adminOption(params.get('oldestFirst'), ['true']),
    } })),
    distinctUntilChanged((left, right) => left.page === right.page && left.filters.search === right.filters.search &&
      left.filters.status === right.filters.status && left.filters.paymentStatus === right.filters.paymentStatus &&
      left.filters.oldestFirst === right.filters.oldestFirst),
    tap(query => this.filters.setValue(query.filters, { emitEvent: false })),
  );
  protected readonly orders = loadAdminData(this.query, query => this.api.orders(query.page, query.filters).pipe(
    map(page => ({ page, filtered: !!(query.filters.search || query.filters.status || query.filters.paymentStatus) })),
  ));

  protected apply(): void {
    const { search, status, paymentStatus, oldestFirst } = this.filters.getRawValue();
    void this.router.navigate([], { relativeTo: this.route, queryParams: {
      search: adminSearch(search) || null, status: status || null, paymentStatus: paymentStatus || null,
      oldestFirst: oldestFirst || null,
    } });
  }

  protected changePage(page: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParamsHandling: 'merge', queryParams: { page: page > 1 ? page : null } });
  }

  protected clear(): void {
    this.filters.reset();
    this.apply();
  }
}
