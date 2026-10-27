import { Routes } from '@angular/router';
import { settingsResolver } from './core/api/settings.service';
import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';
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
        canActivate: [adminGuard],
        loadComponent: () => import('./features/products/product-form').then((m) => m.ProductForm),
      },
      {
        path: 'products/:id/edit',
        title: 'Edit product',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/products/product-form').then((m) => m.ProductForm),
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
        canActivate: [adminGuard],
        loadComponent: () => import('./features/reports/reports').then((m) => m.Reports),
      },
      {
        path: 'users',
        title: 'Users',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/users/user-list').then((m) => m.UserList),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
