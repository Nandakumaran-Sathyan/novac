import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LoginResponse, RegisterRequest, User } from '../models';

const STORAGE_KEY = 'smartpos.session';

interface Session {
  accessToken: string;
  expiresAt: string;
  user: User;
}

/**
 * Holds the signed-in session. The JWT is kept in localStorage so a page refresh doesn't sign the
 * user out mid-shift; the trade-off (readable by any script on the page, so XSS matters) is
 * discussed in docs/ARCHITECTURE.md. The server re-checks the user on every request.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(readStoredSession());
  private expiryTimer?: ReturnType<typeof setTimeout>;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  constructor() {
    this.scheduleExpiry();
  }

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/login`, { email, password })
      .pipe(tap((response) => this.setSession(response)));
  }

  /** Creates an account together with its shop and signs in. */
  register(request: RegisterRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/register`, request)
      .pipe(tap((response) => this.setSession(response)));
  }

  /** Confirms an email address with the token from the emailed link. */
  verifyEmail(token: string): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/auth/verify-email`, { token });
  }

  resendVerification(): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/auth/resend-verification`, {});
  }

  /** Re-reads the signed-in user's profile (e.g. after verifying their email) and updates the stored session. */
  refreshUser(): Observable<User> {
    return this.http.get<User>(`${environment.apiUrl}/auth/me`).pipe(
      tap((user) => {
        const session = this.session();
        if (session) {
          this.setSession({ ...session, user });
        }
      }),
    );
  }

  logout(reason?: 'expired'): void {
    this.setSession(null);
    void this.router.navigate(['/login'], { queryParams: reason ? { reason } : {} });
  }

  private setSession(session: Session | null): void {
    this.session.set(session);
    try {
      if (session) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
      } else {
        localStorage.removeItem(STORAGE_KEY);
      }
    } catch {
      // Storage unavailable (private mode): the session still works for this tab.
    }
    this.scheduleExpiry();
  }

  private scheduleExpiry(): void {
    clearTimeout(this.expiryTimer);
    const session = this.session();
    if (session) {
      const msLeft = Date.parse(session.expiresAt) - Date.now();
      this.expiryTimer = setTimeout(() => this.logout('expired'), Math.max(msLeft, 0));
    }
  }
}

function readStoredSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const session = JSON.parse(raw) as Session;
    return Date.parse(session.expiresAt) > Date.now() ? session : null;
  } catch {
    return null;
  }
}
