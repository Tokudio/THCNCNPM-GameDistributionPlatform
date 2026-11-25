import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE, REQUIRES_AUTH } from '../auth/auth-api';
import { AdminOrderDetail, AdminOrderSummary, AdminUserDetail, AdminUserSummary, DashboardSummary } from '../models/admin.models';
import { Page } from '../models/game-store.models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE}/admin`;

  dashboard() {
    return this.http.get<DashboardSummary>(`${this.base}/dashboard/summary`, this.options());
  }

  orders(page: number, filters: Record<string, string>) {
    return this.http.get<Page<AdminOrderSummary>>(`${this.base}/orders`, {
      ...this.options(), params: this.pageParams(page, filters),
    });
  }

  order(id: string) {
    return this.http.get<AdminOrderDetail>(`${this.base}/orders/${encodeURIComponent(id)}`, this.options());
  }

  users(page: number, filters: Record<string, string>) {
    return this.http.get<Page<AdminUserSummary>>(`${this.base}/users`, {
      ...this.options(), params: this.pageParams(page, filters),
    });
  }

  user(id: string) {
    return this.http.get<AdminUserDetail>(`${this.base}/users/${encodeURIComponent(id)}`, this.options());
  }

  updateUserStatus(id: string, isActive: boolean) {
    return this.http.patch<void>(`${this.base}/users/${encodeURIComponent(id)}/status`, { isActive }, this.options());
  }

  private options() {
    // Interceptor hiện có chỉ gắn JWT cho request nội bộ được đánh dấu yêu cầu xác thực.
    return { context: new HttpContext().set(REQUIRES_AUTH, true), timeout: 15_000 };
  }

  private pageParams(page: number, filters: Record<string, string>) {
    return new HttpParams({ fromObject: {
      pageNumber: page, pageSize: 12,
      ...Object.fromEntries(Object.entries(filters).filter(([, value]) => value !== '')),
    } });
  }
}
