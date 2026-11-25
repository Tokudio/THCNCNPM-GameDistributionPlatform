import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { AdminService } from '../../../core/services/admin.service';
import { formatMoney, minorUnits } from '../../store/store-presentation';
import { loadAdminData } from '../admin-data';
import { orderStatusLabel, orderStatusTone } from '../admin-presentation';

@Component({
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dashboard {
  private readonly api = inject(AdminService);
  protected readonly report = loadAdminData(of(null), () => this.api.dashboard());
  protected readonly money = formatMoney;
  protected readonly statusLabel = orderStatusLabel;
  protected readonly statusTone = orderStatusTone;
  protected readonly selectedCurrency = signal('VND');

  protected readonly chart = computed(() => {
    const state = this.report.state();
    if (state.kind !== 'ready') return null;
    const { revenue, dailyRevenue, revenueStartDate } = state.data;
    const currency = revenue.find(row => row.currency === this.selectedCurrency())?.currency ?? revenue[0]?.currency ?? 'VND';
    const amounts = new Map(dailyRevenue.filter(row => row.currency === currency).map(row => [row.date, row.amount]));
    if (amounts.size === 0) return { currency, points: [] };
    const start = new Date(`${revenueStartDate}T00:00:00Z`);
    const days = Array.from({ length: 7 }, (_, index) => {
      const date = new Date(start);
      date.setUTCDate(start.getUTCDate() + index);
      const isoDate = date.toISOString().slice(0, 10);
      return { date: isoDate, amount: amounts.get(isoDate) ?? '0' };
    });
    const largest = days.reduce((max, day) => minorUnits(day.amount) > max ? minorUnits(day.amount) : max, 1n);
    // Chỉ chuyển tỷ lệ đã thu nhỏ về number để vẽ. Số tiền vẫn giữ nguyên dạng chuỗi và BigInt.
    const points = days.map(day => ({ ...day, height: Number(minorUnits(day.amount) * 1000n / largest) }));
    return { currency, points };
  });
}
