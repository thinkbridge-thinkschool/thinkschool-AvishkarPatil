// ── Day-14 piece-1 — AuthBarComponent (modern auth card) ─────────────────
//
// Vertical "Welcome Back" card design with full-width fields.
// Dark mode toggle lives here (both signed-in and signed-out states) so the
// nav is not cluttered.
//
// Seeded writer:  demo@example.com / P@ssw0rd!   (role "writer" → has scope)
// Seeded viewer:  reader@example.com / P@ssw0rd! (role "viewer" → 403 on POST)
// ─────────────────────────────────────────────────────────────────────────

import { Component, inject, signal }              from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AppError }    from '../models/app-error';
import { AuthService } from '../auth.service';
import { ThemeService } from '../theme.service';
import { ToastService } from '../toast.service';

@Component({
  selector: 'app-auth-bar',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <section class="auth-bar" aria-label="Authentication">

      <!-- ── Signed-in state ────────────────────────────────────── -->
      @if (auth.isAuthenticated()) {
        <div class="auth-card signed-in-card">
          <div class="signed-in-row" role="status">
            <div class="user-info">
              <span class="user-avatar" aria-hidden="true">{{ userInitial() }}</span>
              <div class="user-details">
                <span class="user-label">Signed in as</span>
                <span class="user-email">{{ auth.email() }}</span>
              </div>
            </div>
            <div class="signed-in-actions">
              <button class="theme-btn"
                      type="button"
                      (click)="theme.toggle()"
                      [attr.aria-label]="theme.dark() ? 'Switch to light mode' : 'Switch to dark mode'"
                      [title]="theme.dark() ? 'Light mode' : 'Dark mode'">
                {{ theme.dark() ? '☀' : '🌙' }}
              </button>
              <button type="button" class="btn-signout" (click)="auth.logout()">
                Sign out
              </button>
            </div>
          </div>
        </div>

      <!-- ── Signed-out state: "Welcome Back" card ──────────────── -->
      } @else {
        <div class="auth-card">

          <!-- Header row: welcome text + theme toggle -->
          <div class="card-header-row">
            <div class="card-title">
              <p class="welcome-heading">Welcome Back</p>
              <p class="welcome-sub">Sign in to create and manage quotes</p>
            </div>
            <button class="theme-btn"
                    type="button"
                    (click)="theme.toggle()"
                    [attr.aria-label]="theme.dark() ? 'Switch to light mode' : 'Switch to dark mode'"
                    [title]="theme.dark() ? 'Light mode' : 'Dark mode'">
              {{ theme.dark() ? '☀' : '🌙' }}
            </button>
          </div>

          <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate aria-label="Sign in">

            <!-- Email -->
            <div class="field">
              <label for="login-email">Email</label>
              <input id="login-email" type="email" formControlName="email"
                     autocomplete="username"
                     placeholder="you@example.com"
                     [attr.aria-invalid]="invalid('email') ? 'true' : null"
                     [attr.aria-describedby]="invalid('email') ? 'login-email-error' : null" />
              <!-- Always in DOM; visibility toggled so height is always reserved -->
              <span class="field-error"
                    id="login-email-error"
                    [class.field-error--on]="invalid('email')"
                    [attr.aria-hidden]="!invalid('email') ? 'true' : null">
                A valid email is required.
              </span>
            </div>

            <!-- Password -->
            <div class="field">
              <label for="login-password">Password</label>
              <input id="login-password" type="password" formControlName="password"
                     autocomplete="current-password"
                     placeholder="••••••••"
                     [attr.aria-invalid]="invalid('password') ? 'true' : null"
                     [attr.aria-describedby]="invalid('password') ? 'login-password-error' : null" />
              <span class="field-error"
                    id="login-password-error"
                    [class.field-error--on]="invalid('password')"
                    [attr.aria-hidden]="!invalid('password') ? 'true' : null">
                Password is required.
              </span>
            </div>

            <!-- Submit -->
            <button type="submit" class="btn-submit"
                    [disabled]="busy()" [attr.aria-busy]="busy() ? 'true' : null">
              {{ busy() ? 'Signing in…' : 'Sign In' }}
            </button>

            <div aria-live="assertive">
              @if (error()) {
                <p class="server-error" role="alert">{{ error() }}</p>
              }
            </div>
          </form>

          <p class="demo-hint">
            Demo: <code>demo&#64;example.com</code> / <code>P&#64;ssw0rd!</code>
          </p>
        </div>
      }
    </section>
  `,
  styles: [`
    /* ── Wrapper ────────────────────────────────────────────────── */
    .auth-bar { margin-bottom: 1.5rem; }

    /* ── Card shell ─────────────────────────────────────────────── */
    .auth-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 14px;
      padding: 1.5rem;
      box-shadow: var(--shadow-sm);
      max-width: 480px;
      transition: box-shadow 0.2s;
    }

    /* ── Header: "Welcome Back" + theme toggle ───────────────────── */
    .card-header-row {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 0.75rem;
      margin-bottom: 1.5rem;
    }
    .welcome-heading {
      font-size: 1.3rem;
      font-weight: 700;
      color: var(--text-primary);
      margin: 0 0 0.2rem;
      line-height: 1.2;
    }
    .welcome-sub {
      font-size: 0.8rem;
      color: var(--text-muted);
      margin: 0;
    }

    /* ── Theme toggle (used in both states) ──────────────────────── */
    .theme-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.1rem;
      height: 2.1rem;
      font-size: 1rem;
      line-height: 1;
      flex-shrink: 0;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--surface-subtle);
      color: var(--text-primary);
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s, transform 0.12s;
    }
    .theme-btn:hover {
      background: var(--border);
      border-color: var(--border-muted);
      transform: scale(1.08);
    }
    .theme-btn:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }

    /* ── Vertical field stack ────────────────────────────────────── */
    .field { display: flex; flex-direction: column; margin-bottom: 1rem; }

    label {
      display: block;
      font-weight: 600;
      font-size: 0.78rem;
      color: var(--text-secondary);
      margin-bottom: 0.35rem;
      letter-spacing: 0.02em;
      text-transform: uppercase;
    }

    input {
      font: inherit;
      font-size: 0.9rem;
      padding: 0.5rem 0.7rem;
      border: 1px solid var(--border-muted);
      border-radius: 8px;
      background: var(--surface);
      color: var(--text-primary);
      width: 100%;
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    input::placeholder { color: var(--text-disabled); font-style: italic; }
    input:focus {
      outline: none;
      border-color: var(--primary);
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    input[aria-invalid='true'] {
      border-color: var(--danger);
      box-shadow: 0 0 0 2px var(--danger-ring);
    }

    /* Error span always in DOM; visibility reserves the line height */
    .field-error {
      display: block;
      font-size: 0.72rem;
      line-height: 1.3;
      height: calc(0.72rem * 1.3);
      margin-top: 0.25rem;
      overflow: hidden;
      white-space: nowrap;
      color: var(--danger);
      visibility: hidden;
    }
    .field-error--on { visibility: visible; }

    /* ── Full-width primary Sign In button ───────────────────────── */
    .btn-submit {
      width: 100%;
      font: inherit;
      font-size: 0.9rem;
      font-weight: 600;
      padding: 0.65rem;
      border: 0;
      border-radius: 8px;
      background: var(--primary);
      color: #fff;
      cursor: pointer;
      margin-top: 0.25rem;
      transition: background 0.15s, box-shadow 0.15s, transform 0.12s;
    }
    .btn-submit:hover:not(:disabled) {
      background: var(--primary-dark);
      box-shadow: var(--shadow-hover);
      transform: translateY(-1px);
    }
    .btn-submit:active:not(:disabled) { transform: translateY(0); }
    .btn-submit:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .btn-submit:disabled { background: var(--text-muted); cursor: progress; opacity: 0.75; }

    /* ── Server error ────────────────────────────────────────────── */
    .server-error { color: var(--danger); font-size: 0.8rem; margin-top: 0.6rem; }

    /* ── Demo credentials hint ───────────────────────────────────── */
    .demo-hint {
      font-size: 0.74rem;
      color: var(--text-muted);
      margin: 1rem 0 0;
      text-align: center;
    }
    .demo-hint code {
      background: var(--code-bg);
      padding: 0.05rem 0.3rem;
      border-radius: 4px;
      font-size: 0.72rem;
      font-family: ui-monospace, 'Cascadia Mono', 'Segoe UI Mono', monospace;
    }

    /* ── Signed-in card ──────────────────────────────────────────── */
    .signed-in-card {
      background: var(--success-bg);
      border-color: var(--success-border);
      padding: 0.875rem 1.25rem;
      max-width: 100%;
    }
    .signed-in-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
    }
    .user-info {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .user-avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.25rem;
      height: 2.25rem;
      border-radius: 50%;
      background: var(--primary);
      color: #fff;
      font-size: 0.9rem;
      font-weight: 700;
      flex-shrink: 0;
      letter-spacing: 0.02em;
    }
    .user-details { display: flex; flex-direction: column; gap: 0.05rem; }
    .user-label {
      font-size: 0.68rem;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .user-email {
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--text-primary);
    }
    .signed-in-actions { display: flex; align-items: center; gap: 0.5rem; }

    .btn-signout {
      font: inherit;
      font-size: 0.8rem;
      font-weight: 500;
      padding: 0.35rem 0.875rem;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface);
      color: var(--text-secondary);
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s, transform 0.1s;
    }
    .btn-signout:hover {
      background: var(--surface-hover);
      border-color: var(--border-muted);
      transform: translateY(-1px);
    }
    .btn-signout:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }

    /* ── Responsive ─────────────────────────────────────────────── */
    @media (max-width: 520px) {
      .auth-card        { padding: 1.25rem; border-radius: 10px; }
      .welcome-heading  { font-size: 1.15rem; }
    }
    @media (max-width: 420px) {
      .signed-in-row    { flex-direction: column; align-items: flex-start; }
      .signed-in-actions { width: 100%; justify-content: flex-end; }
    }
  `],
})
export class AuthBarComponent {
  protected readonly auth     = inject(AuthService);
  protected readonly theme    = inject(ThemeService);
  private   readonly fb       = inject(FormBuilder);
  private   readonly toastSvc = inject(ToastService);

  protected readonly busy  = signal<boolean>(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected userInitial(): string {
    const e = this.auth.email();
    return e ? e[0].toUpperCase() : 'U';
  }

  protected invalid(name: 'email' | 'password'): boolean {
    const c = this.form.controls[name];
    return c.invalid && c.touched;
  }

  protected async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    try {
      await this.auth.login(email, password);
      this.form.reset();
      this.toastSvc.show('Signed in successfully', 'success');
    } catch (err: unknown) {
      const msg = err instanceof AppError
        ? err.message
        : 'Sign-in failed. Is the API running on :5075?';
      this.error.set(msg);
      this.toastSvc.show(msg, 'error');
    } finally {
      this.busy.set(false);
    }
  }
}
