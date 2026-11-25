import { OrderStatus, PaymentStatus } from '../../core/models/order.models';

export const orderStatusLabel = (status: OrderStatus): string => ({
  [OrderStatus.Pending]: 'Chờ thanh toán', [OrderStatus.Paid]: 'Đã thanh toán', [OrderStatus.Cancelled]: 'Đã hủy',
})[status] ?? 'Chưa xác định';

export const paymentStatusLabel = (status: PaymentStatus | null): string => status === null ? 'Chưa có giao dịch' : ({
  [PaymentStatus.Pending]: 'Đang chờ', [PaymentStatus.Succeeded]: 'Thành công', [PaymentStatus.Failed]: 'Thất bại',
})[status] ?? 'Chưa xác định';

export const orderStatusTone = (status: OrderStatus): string => status === OrderStatus.Paid ? 'positive' :
  status === OrderStatus.Cancelled ? 'negative' : 'pending';

export const paymentStatusTone = (status: PaymentStatus | null): string => status === PaymentStatus.Succeeded ? 'positive' :
  status === PaymentStatus.Failed ? 'negative' : status === PaymentStatus.Pending ? 'pending' : '';

export const paymentMethodLabel = (method: number): string => method === 0 ? 'Thanh toán mô phỏng' :
  method === 1 ? 'VnPay' : 'Chưa xác định';

export const accountStatusLabel = (active: boolean): string => active ? 'Đang hoạt động' : 'Tạm khóa';

export const accountInitial = (name: string, email: string | null): string =>
  (name.trim() || email || '?').charAt(0).toUpperCase();
