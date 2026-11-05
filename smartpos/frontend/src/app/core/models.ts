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
  emailVerified: boolean;
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
  /** Days from ordering to delivery (used by forecasting). */
  leadTimeDays: number;
  /** Supplier's minimum order / case size (used by forecasting). */
  minimumOrderQuantity: number;
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
  leadTimeDays: number;
  minimumOrderQuantity: number;
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
  /** Final amount for the line: unit price × quantity − discount. */
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
  /** Points this bill earned for its customer (0 for walk-ins). */
  loyaltyPointsEarned: number;
  /** The customer's balance right now, or null for a walk-in. */
  customerLoyaltyPoints: number | null;
  subtotal: number;
  discountPercent: number;
  discountAmount: number;
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
  maxDiscountPercent: number;
  /** Amount spent to earn one loyalty point. */
  loyaltySpendPerPoint: number;
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
  /** Net sales: after discount. */
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

/** One earlier turn sent back for context. The server accepts only these two roles. */
export interface AIChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface AIChatRequest {
  message: string;
  history: AIChatTurn[];
}

export interface AIChatResponse {
  reply: string;
  /** Names of the read-only data tools the assistant ran, e.g. GetTodaySales. */
  toolsUsed: string[];
  /** The model hit its output limit, so the answer may be cut off. */
  truncated: boolean;
  durationMs: number;
}

export interface AIStatus {
  reachable: boolean;
  model: string;
  modelInstalled: boolean;
  supportsTools: boolean;
  serverVersion: string | null;
  problem: string | null;
  ready: boolean;
}

// ---- Forecasting (beta; the signed-in owner's own shop, served by the forecasting service through /api/forecasting) ----

export type StockoutRisk = 'HIGH' | 'MEDIUM' | 'LOW';

export interface ForecastRun {
  run_id: string;
  model_version: string;
  generated_at: string;
  as_of_date: string;
  forecast_start: string;
  horizon: number;
  /** Days with at least one completed bill that the forecast learned from. */
  trading_days: number;
}

export interface ForecastStatus {
  shop_id: number;
  beta: boolean;
  note: string;
  model_version: string | null;
  model_trained_at: string | null;
  latest_run: ForecastRun | null;
}

export interface DailyDemand {
  forecast_date: string;
  predicted_demand: number;
}

export interface ProductForecast {
  product_id: number;
  sku: string;
  product_name: string;
  category: string;
  method: string;
  current_stock: number;
  cumulative_predicted_demand: number;
  stockout_risk: StockoutRisk;
  predicted_stockout_date: string | null;
  recommended_reorder_quantity: number;
  model_version: string;
  daily: DailyDemand[];
}

export interface ShopForecast {
  run: ForecastRun;
  shop_id: number;
  horizon: number;
  total_predicted_demand: number;
  daily_totals: DailyDemand[];
  items: ProductForecast[];
}

export interface StockoutAlert {
  product_id: number;
  sku: string;
  product_name: string;
  category: string;
  current_stock: number;
  inventory_known: boolean;
  cumulative_predicted_demand: number;
  days_of_cover: number | null;
  predicted_stockout_date: string | null;
  stockout_risk: StockoutRisk;
  lead_days: number;
  safety_stock: number;
  reason: string;
  model_version: string;
}

export interface StockoutAlerts {
  run: ForecastRun;
  shop_id: number;
  risk_note: string;
  counts: Record<StockoutRisk, number>;
  items: StockoutAlert[];
}

export interface ReorderRecommendation {
  product_id: number;
  sku: string;
  product_name: string;
  category: string;
  current_stock: number;
  on_order: number;
  cumulative_predicted_demand: number;
  safety_stock: number;
  reorder_point: number;
  order_up_to: number;
  lead_days: number;
  minimum_order_quantity: number;
  recommended_reorder_quantity: number;
  order_by_date: string | null;
  stockout_risk: StockoutRisk;
  reason: string;
  model_version: string;
}

export interface ReorderRecommendations {
  run: ForecastRun;
  shop_id: number;
  items: ReorderRecommendation[];
}

export interface ForecastMetric {
  evaluation: string;
  method: string;
  target: string;
  /** 'overall' = every shop the model learned from; 'shop' = this shop only. */
  scope: 'overall' | 'shop';
  scope_id: string;
  mae: number | null;
  rmse: number | null;
  wape: number | null;
  bias: number | null;
  n_obs: number;
}

export interface ForecastModelMetrics {
  model_version: string;
  trained_at: string;
  train_start: string;
  test_end: string | null;
  evaluation_note: string;
  metrics: ForecastMetric[];
}
