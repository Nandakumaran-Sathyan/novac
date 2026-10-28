import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Icon, IconName } from '../shared/components/icon';

interface NavItem {
  label: string;
  path: string;
  icon: IconName;
}

const NAV: NavItem[] = [
  { label: 'Dashboard', path: '/dashboard', icon: 'dashboard' },
  { label: 'Point of Sale', path: '/pos', icon: 'cart' },
  { label: 'Sales', path: '/sales', icon: 'receipt' },
  { label: 'Customers', path: '/customers', icon: 'user' },
  { label: 'Products', path: '/products', icon: 'package' },
  { label: 'Inventory', path: '/inventory', icon: 'layers' },
  { label: 'Reports', path: '/reports', icon: 'chart' },
];

@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, Icon],
  template: `
    <div class="brand">
      <span class="brand-mark" aria-hidden="true">S</span>
      <span class="brand-name">SmartPOS</span>
    </div>
    <nav aria-label="Main navigation">
      <ul>
        @for (item of items(); track item.path) {
          <li>
            <a [routerLink]="item.path" routerLinkActive="active" ariaCurrentWhenActive="page" (click)="navigate.emit()">
              <app-icon [name]="item.icon" />
              <span>{{ item.label }}</span>
            </a>
          </li>
        }
      </ul>
    </nav>
    <div class="sidebar-footer">
      <span class="role-label">{{ auth.user()?.email }}</span>
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; height: 100%; background: var(--color-sidebar); color: var(--color-sidebar-text); }
    .brand { display: flex; align-items: center; gap: 10px; height: var(--header-height); padding: 0 20px; border-bottom: 1px solid rgba(255,255,255,0.06); }
    .brand-mark { display: inline-grid; place-items: center; width: 30px; height: 30px; border-radius: 8px; background: var(--color-primary); color: #fff; font-weight: 700; }
    .brand-name { color: #fff; font-weight: 600; font-size: 1rem; }
    nav { flex: 1; padding: 12px 10px; overflow-y: auto; }
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
    a { display: flex; align-items: center; gap: 12px; height: 40px; padding: 0 12px; border-radius: var(--radius); color: var(--color-sidebar-text); font-weight: 500; text-decoration: none; }
    a:hover { background: var(--color-sidebar-hover); color: #fff; text-decoration: none; }
    a.active { background: rgba(22,163,74,0.16); color: #fff; box-shadow: inset 3px 0 0 var(--color-primary); }
    a.active app-icon { color: #4ade80; }
    .sidebar-footer { padding: 14px 20px; border-top: 1px solid rgba(255,255,255,0.06); }
    .role-label { font-size: 0.75rem; color: var(--color-sidebar-muted); }
  `,
})
export class Sidebar {
  protected readonly auth = inject(AuthService);
  readonly navigate = output<void>();

  protected readonly items = computed(() => {
    return this.auth.isAuthenticated() ? NAV : [];
  });
}
