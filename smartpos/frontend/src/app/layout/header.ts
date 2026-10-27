import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { SettingsService } from '../core/api/settings.service';
import { AuthService } from '../core/auth/auth.service';
import { Icon } from '../shared/components/icon';

@Component({
  selector: 'app-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <button type="button" class="btn btn-ghost btn-icon menu-button" (click)="toggleMenu.emit()" aria-label="Open navigation">
      <app-icon name="menu" />
    </button>
    <span class="store-name">{{ settings.storeName() }}</span>
    <div class="user">
      <span class="avatar" aria-hidden="true">{{ initials() }}</span>
      <span class="user-text">
        <span class="user-name">{{ auth.user()?.fullName }}</span>
        <span class="user-role">{{ auth.user()?.role }}</span>
      </span>
      <button type="button" class="btn btn-ghost btn-sm" (click)="auth.logout()">
        <app-icon name="logout" [size]="16" /> <span class="hide-sm">Sign out</span>
      </button>
    </div>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: 12px; height: var(--header-height); padding: 0 24px; background: var(--color-surface); border-bottom: 1px solid var(--color-border); }
    .menu-button { display: none; }
    .store-name { font-weight: 600; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .user { display: flex; align-items: center; gap: 10px; }
    .avatar { display: inline-grid; place-items: center; width: 32px; height: 32px; border-radius: 999px; background: var(--color-primary-soft); color: var(--color-primary-text); font-weight: 600; font-size: 0.8125rem; }
    .user-text { display: flex; flex-direction: column; line-height: 1.2; }
    .user-name { font-weight: 500; }
    .user-role { font-size: 0.75rem; color: var(--color-text-muted); }
    @media (max-width: 900px) { :host { padding: 0 12px; } .menu-button { display: inline-flex; } }
    @media (max-width: 640px) { .user-text { display: none; } }
  `,
})
export class Header {
  protected readonly auth = inject(AuthService);
  protected readonly settings = inject(SettingsService);
  readonly toggleMenu = output<void>();

  protected readonly initials = computed(() =>
    (this.auth.user()?.fullName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );
}
