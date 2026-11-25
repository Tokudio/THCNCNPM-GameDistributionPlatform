import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, map, tap } from 'rxjs';
import { AdminService } from '../../../../core/services/admin.service';
import { adminOption, adminSearch, loadAdminData, readAdminPage } from '../../admin-data';
import { AdminPager } from '../../admin-pager/admin-pager';
import { accountInitial, accountStatusLabel } from '../../admin-presentation';

@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe, AdminPager],
  templateUrl: './user-list.html',
  styleUrl: './user-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserList {
  private readonly api = inject(AdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly initial = accountInitial;
  protected readonly statusLabel = accountStatusLabel;
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    isActive: new FormControl('', { nonNullable: true }),
    role: new FormControl('', { nonNullable: true }),
  });
  private readonly query = this.route.queryParamMap.pipe(
    map(params => ({ page: readAdminPage(params), filters: {
      search: adminSearch(params.get('search')),
      isActive: adminOption(params.get('isActive'), ['true', 'false']),
      role: adminOption(params.get('role'), ['Admin', 'User']),
    } })),
    distinctUntilChanged((left, right) => left.page === right.page && left.filters.search === right.filters.search &&
      left.filters.isActive === right.filters.isActive && left.filters.role === right.filters.role),
    tap(query => this.filters.setValue(query.filters, { emitEvent: false })),
  );
  protected readonly users = loadAdminData(this.query, query => this.api.users(query.page, query.filters).pipe(
    map(page => ({ page, filtered: !!(query.filters.search || query.filters.isActive || query.filters.role) })),
  ));

  protected apply(): void {
    const { search, isActive, role } = this.filters.getRawValue();
    void this.router.navigate([], { relativeTo: this.route, queryParams: {
      search: adminSearch(search) || null, isActive: isActive || null, role: role || null,
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
