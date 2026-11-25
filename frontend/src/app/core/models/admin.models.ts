import { OrderStatus, PaymentStatus } from './order.models';

export interface AdminOrderSummary {
  id: string;
  orderCode: string;
  customerName: string;
  customerEmail: string | null;
  status: OrderStatus;
  paymentStatus: PaymentStatus | null;
  createdAt: string;
  currency: string;
  totalAmount: string;
}

export interface AdminOrderItem {
  gameId: string;
  gameTitle: string;
  coverImageUrl: string | null;
  unitPrice: string;
}

export interface AdminPayment {
  attemptNumber: number;
  status: PaymentStatus;
  method: number;
  currency: string;
  createdAt: string;
  completedAt: string | null;
  amount: string;
}

export interface AdminOrderDetail {
  order: AdminOrderSummary;
  paidAt: string | null;
  originalAmount: string;
  discountAmount: string;
  items: AdminOrderItem[];
  payments: AdminPayment[];
}

interface Revenue {
  currency: string;
  amount: string;
}

export interface DashboardSummary {
  games: { total: number; published: number; draft: number; archived: number };
  totalCategories: number;
  users: { total: number; active: number; inactive: number };
  orders: { total: number; pending: number; paid: number; cancelled: number };
  revenue: Revenue[];
  revenueStartDate: string;
  dailyRevenue: (Revenue & { date: string })[];
  recentOrders: Pick<AdminOrderSummary, 'id' | 'orderCode' | 'status' | 'createdAt' | 'currency' | 'totalAmount'>[];
  unverifiedPaidOrders: number;
  revenueOrdersMissingPaidAt: number;
}

export interface AdminUserSummary {
  id: string;
  displayName: string;
  email: string | null;
  isActive: boolean;
  createdAt: string;
  roles: string[];
}

export interface AdminUserDetail {
  user: AdminUserSummary;
  totalOrders: number;
  ownedGames: number;
}
