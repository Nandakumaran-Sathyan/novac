import { Routes } from '@angular/router';
import { settingsResolver } from './core/api/settings.service';
import { authGuard, guestGuard } from './core/auth/auth.guards';
import { Shell } from './layout/shell';

// Feature pages are lazy-loaded so the login screen downloads only what it needs.
export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'verify-email',
    title: 'Verify email',
    loadComponent: () => import('./features/auth/verify-email').then((m) => m.VerifyEmail),
  },
  {
    path: 'register',
    title: 'Create account',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register').then((m) => m.Register),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    resolve: { settings: settingsResolver },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'pos',
        title: 'Point of Sale',
        loadComponent: () => import('./features/pos/pos').then((m) => m.Pos),
      },
      {
        path: 'products',
        title: 'Products',
        loadComponent: () => import('./features/products/product-list').then((m) => m.ProductList),
      },
      {
        path: 'products/new',
        title: 'New product',
        loadComponent: () => import('./features/products/product-form').then((m) => m.ProductForm),
      },
      {
        path: 'products/:id/edit',
        title: 'Edit product',
        loadComponent: () => import('./features/products/product-form').then((m) => m.ProductForm),
      },
      {
        path: 'customers',
        title: 'Customers',
        loadComponent: () => import('./features/customers/customer-list').then((m) => m.CustomerList),
      },
      {
        path: 'inventory',
        title: 'Inventory',
        loadComponent: () => import('./features/inventory/inventory').then((m) => m.Inventory),
      },
      {
        path: 'inventory/movements',
        title: 'Stock movements',
        loadComponent: () => import('./features/inventory/movements').then((m) => m.Movements),
      },
      {
        path: 'sales',
        title: 'Sales',
        loadComponent: () => import('./features/sales/sales-list').then((m) => m.SalesList),
      },
      {
        path: 'sales/:id',
        title: 'Invoice',
        loadComponent: () => import('./features/sales/sale-detail').then((m) => m.SaleDetail),
      },
      {
        path: 'reports',
        title: 'Reports',
        loadComponent: () => import('./features/reports/reports').then((m) => m.Reports),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
