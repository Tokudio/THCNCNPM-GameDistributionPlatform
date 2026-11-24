export enum OrderStatus { Pending, Paid, Cancelled }
export enum PaymentStatus { Pending, Succeeded, Failed }
export enum MockPaymentOutcome { Success, Failure }

export interface OrderItem {
  gameId: string;
  gameTitle: string;
  coverImageUrl: string | null;
  categories: string[];
  unitPrice: string;
}

export interface Order {
  id: string;
  orderCode: string;
  status: OrderStatus;
  currency: string;
  createdAt: string;
  paidAt: string | null;
  originalAmount: string;
  discountAmount: string;
  totalAmount: string;
  items: OrderItem[];
  paymentStatus: PaymentStatus | null;
  paymentMessage: string | null;
  ownershipGranted: boolean;
  mockPaymentAvailable: boolean;
}

export interface MockPaymentResult {
  orderId: string;
  orderStatus: OrderStatus;
  paymentStatus: PaymentStatus;
  currency: string;
  amount: string;
  message: string;
}
