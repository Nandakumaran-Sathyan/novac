// TypeScript mirrors of the API's DTOs (SmartPOS.Application). Dates are ISO-8601 strings in UTC.

export type PaymentMethod = 'Cash' | 'Card' | 'UPI' | 'Other';
export type SaleStatus = 'Pending' | 'Completed' | 'Cancelled';
export type StockStatus = 'InStock' | 'Low' | 'OutOfStock';
export type StockMovementType = 'InitialStock' | 'Sale' | 'Adjustment';
export type InventoryFilter = 'All' | 'Low' | 'OutOfStock';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PageQuery {
  page?: number;
  pageSize?: number;
}

export interface User {
  id: number;
  fullName: string;
  email: string;
  shopId: number;
  shopName: string;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  shopName: string;
  shopAddress: string | null;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

/** A category is just the text on a product; this is one distinct name with its product count. */
export interface Category {
  name: string;
  productCount: number;
}

export interface Customer {
  id: number;
  name: string;
  phone: string;
  loyaltyPoints: number;
  createdAt: string;
}

export interface CustomerInput {
  name: string;
  phone: string;
}

export interface Product {
  id: number;
  sku: string;
  barcode: string | null;
  name: string;
  description: string | null;
  category: string;
  price: number;
  costPrice: number;
  stockQuantity: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  /** Calendar date, yyyy-MM-dd. */
  expiryDate: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ProductInput {
  sku: string;
  barcode: string | null;
  name: string;
  description: string | null;
  category: string;
  price: number;
  costPrice: number;
  lowStockThreshold: number;
  expiryDate: string | null;
  /** Create only. */
  initialStock?: number;
  /** Update only. */
  isActive?: boolean;
}

export interface InventoryItem {
  productId: number;
  sku: string;
  name: string;
  category: string;
  stockQuantity: number;
  lowStockThreshold: number;
  status: StockStatus;
  isActive: boolean;
  lastUpdated: string;
}

export interface StockMovement {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  quantityChange: number;
  quantityAfter: number;
  type: StockMovementType;
  reason: string;
  saleId: number | null;
  billNumber: string | null;
  userName: string | null;
  createdAt: string;
}

export interface SaleItem {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  /** This line's share of the sale discount. */
  discountAmount: number;
  taxAmount: number;
  /** Final amount for the line: unit price × quantity − discount + tax. */
  lineTotal: number;
}

export interface Sale {
  id: number;
  billNumber: string;
  createdAt: string;
  userId: number;
  cashierName: string;
  customerId: number | null;
  customerName: string | null;
  status: SaleStatus;
  paymentMethod: PaymentMethod;
  subtotal: number;
  discountPercent: number;
  discountAmount: number;
  taxRatePercent: number;
  taxAmount: number;
  totalAmount: number;
  amountPaid: number;
  changeAmount: number;
  items: SaleItem[];
}

export interface SaleSummary {
  id: number;
  billNumber: string;
  createdAt: string;
  cashierName: string;
  customerName: string | null;
  itemCount: number;
  status: SaleStatus;
  paymentMethod: PaymentMethod;
  totalAmount: number;
}

export interface CreateSaleRequest {
  items: { productId: number; quantity: number }[];
  discountPercent: number;
  paymentMethod: PaymentMethod;
  /** Omit for "exactly the total". Cash may exceed the total; the difference is change. */
  amountPaid?: number;
  customerId?: number;
}

export interface PosSettings {
  storeName: string;
  storeAddress: string;
  currencyCode: string;
  timeZoneId: string;
  taxRatePercent: number;
  maxDiscountPercent: number;
}

export interface DashboardSummary {
  date: string;
  todaySalesTotal: number;
  todayTransactionCount: number;
  productCount: number;
  lowStockCount: number;
  recentTransactions: SaleSummary[];
  lowStockProducts: InventoryItem[];
}

export interface Kpi {
  current: number;
  previous: number;
}

export interface InsightKpis {
  revenue: Kpi;
  bills: Kpi;
  averageBill: Kpi;
  itemsSold: Kpi;
  customers: Kpi;
  grossProfit: Kpi;
}

export interface DailyPoint {
  date: string;
  revenue: number;
  bills: number;
  /** The matching day of the previous period. */
  previousRevenue: number;
}

export interface PaymentSlice {
  method: PaymentMethod;
  bills: number;
  amount: number;
}

export interface CategorySlice {
  category: string;
  units: number;
  /** Net sales: after discount, before tax. */
  revenue: number;
}

export interface ProductPerformance {
  productId: number;
  sku: string;
  name: string;
  units: number;
  revenue: number;
  grossProfit: number;
}

export interface CustomerPerformance {
  customerId: number;
  name: string;
  bills: number;
  spent: number;
}

export interface Insights {
  from: string;
  to: string;
  days: number;
  kpis: InsightKpis;
  daily: DailyPoint[];
  /** Bill counts: 7 weekdays (Monday first) × 24 hours, in store time. */
  heatmap: number[][];
  payments: PaymentSlice[];
  categories: CategorySlice[];
  topProducts: ProductPerformance[];
  topCustomers: CustomerPerformance[];
  billsWithCustomer: number;
  billsWalkIn: number;
}

export interface DailySalesRow {
  date: string;
  transactionCount: number;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
}

export interface DailySalesReport {
  from: string;
  to: string;
  days: DailySalesRow[];
  transactionCount: number;
  total: number;
  averageSale: number;
}

export interface TopProductRow {
  productId: number;
  sku: string;
  productName: string;
  quantitySold: number;
  revenue: number;
  cost: number;
  grossProfit: number;
}
