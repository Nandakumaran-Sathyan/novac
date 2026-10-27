// TypeScript mirrors of the API's DTOs (SmartPOS.Application). Dates are ISO-8601 strings in UTC.

export type UserRole = 'Admin' | 'Cashier';
export type PaymentMethod = 'Cash' | 'Card' | 'Other';
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
  role: UserRole;
  isActive: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface Category {
  id: number;
  name: string;
  description: string | null;
  productCount: number;
}

export interface Product {
  id: number;
  sku: string;
  name: string;
  description: string | null;
  categoryId: number;
  categoryName: string;
  sellingPrice: number;
  costPrice: number;
  stockQuantity: number;
  reorderLevel: number;
  isLowStock: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ProductInput {
  sku: string;
  name: string;
  description: string | null;
  categoryId: number;
  sellingPrice: number;
  costPrice: number;
  reorderLevel: number;
  /** Create only. */
  initialStock?: number;
  /** Update only. */
  isActive?: boolean;
}

export interface InventoryItem {
  productId: number;
  sku: string;
  name: string;
  categoryName: string;
  stockQuantity: number;
  reorderLevel: number;
  status: StockStatus;
  isActive: boolean;
  updatedAt: string;
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
  invoiceNumber: string | null;
  userName: string | null;
  createdAt: string;
}

export interface SaleItem {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface Sale {
  id: number;
  invoiceNumber: string;
  createdAt: string;
  cashierId: number;
  cashierName: string;
  paymentMethod: PaymentMethod;
  subtotal: number;
  discountPercent: number;
  discountAmount: number;
  taxRatePercent: number;
  taxAmount: number;
  total: number;
  items: SaleItem[];
}

export interface SaleSummary {
  id: number;
  invoiceNumber: string;
  createdAt: string;
  cashierName: string;
  itemCount: number;
  paymentMethod: PaymentMethod;
  total: number;
}

export interface CreateSaleRequest {
  items: { productId: number; quantity: number }[];
  discountPercent: number;
  paymentMethod: PaymentMethod;
}

export interface PosSettings {
  storeName: string;
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
