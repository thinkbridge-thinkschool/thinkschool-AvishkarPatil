import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient }                            from '@angular/common/http';
import { firstValueFrom }                        from 'rxjs';
import { LoginResponse }                         from './models/auth';

// Minimal auth for Day-14: just enough to obtain a writer token so the
// create-a-quote form can satisfy the can-edit-quotes policy on POST
// /api/quotes. The access token lives in a signal in memory only — no
// refresh handling, no persistence; this piece is about the form, not auth.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly token = signal<string | null>(null);
  readonly email = signal<string | null>(null);
  readonly isAuthenticated = computed<boolean>(() => this.token() !== null);

  async login(email: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<LoginResponse>('/api/auth/login', { email, password }),
    );
    this.token.set(res.accessToken);
    this.email.set(email);
  }

  logout(): void {
    this.token.set(null);
    this.email.set(null);
  }
}
