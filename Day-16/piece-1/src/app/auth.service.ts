import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient }                            from '@angular/common/http';
import { firstValueFrom }                        from 'rxjs';
import { LoginResponse }                         from './models/auth';

// Auth for Day-16: token + email persisted to sessionStorage so the auth
// guard can be tested by typing /quotes/new directly (address-bar navigation
// triggers a full page reload, which would otherwise reset the in-memory
// signal and make the guard always redirect — hiding whether it works).
// sessionStorage is cleared when the tab closes, so there is no cross-session
// token leak. No refresh handling — expiry is not checked.
const STORAGE_TOKEN = 'auth_token';
const STORAGE_EMAIL = 'auth_email';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly token = signal<string | null>(sessionStorage.getItem(STORAGE_TOKEN));
  readonly email = signal<string | null>(sessionStorage.getItem(STORAGE_EMAIL));
  readonly isAuthenticated = computed<boolean>(() => this.token() !== null);

  async login(email: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<LoginResponse>('/api/auth/login', { email, password }),
    );
    sessionStorage.setItem(STORAGE_TOKEN, res.accessToken);
    sessionStorage.setItem(STORAGE_EMAIL, email);
    this.token.set(res.accessToken);
    this.email.set(email);
  }

  logout(): void {
    sessionStorage.removeItem(STORAGE_TOKEN);
    sessionStorage.removeItem(STORAGE_EMAIL);
    this.token.set(null);
    this.email.set(null);
  }
}
