import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PosSettings } from '../models';

/** Store/POS settings for the signed-in user (currency, tax rate, discount limit). */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly state = signal<PosSettings | null>(null);

  readonly settings = this.state.asReadonly();
  readonly currencyCode = computed(() => this.state()?.currencyCode ?? 'USD');
  readonly storeName = computed(() => this.state()?.storeName ?? 'SmartPOS');

  load(): Observable<PosSettings> {
    return this.http.get<PosSettings>(`${environment.apiUrl}/settings/pos`).pipe(tap((s) => this.state.set(s)));
  }
}

/** Loads settings before the app shell renders, so money formatting is right from the first paint. */
export const settingsResolver: ResolveFn<PosSettings> = () => inject(SettingsService).load();
