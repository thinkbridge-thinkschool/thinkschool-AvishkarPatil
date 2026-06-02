// ── Day-14 piece-1 — AuthBarComponent (minimal sign-in) ───────────────────
//
// A compact reactive sign-in so the create-a-quote form can obtain a writer
// token (scope=quotes.write) and POST /api/quotes returns 201 instead of 401.
// Same a11y conventions as the quote form: associated labels, aria-invalid /
// aria-describedby on error, aria-live for the auth error, keyboard-operable.
//
// Seeded writer:  demo@example.com / P@ssw0rd!   (role "writer" → has scope)
// Seeded viewer:  reader@example.com / P@ssw0rd! (role "viewer" → 403 on POST)
// ─────────────────────────────────────────────────────────────────────────

import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse }          from '@angular/common/http';
import { AuthService }                from '../auth.service';

@Component({
  selector: 'app-auth-bar',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <section class="auth-bar" aria-label="Authentication">
      @if (auth.isAuthenticated()) {
        <p class="signed-in" role="status">
          Signed in as <strong>{{ auth.email() }}</strong>.
          <button type="button" (click)="auth.logout()">Sign out</button>
        </p>
      } @else {
        <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate aria-label="Sign in">
          <div class="row">
            <div class="field">
              <label for="login-email">Email</label>
              <input id="login-email" type="email" formControlName="email"
                     autocomplete="username" aria-required="true"
                     [attr.aria-invalid]="invalid('email') ? 'true' : null"
                     [attr.aria-describedby]="invalid('email') ? 'login-email-error' : null" />
              @if (invalid('email')) {
                <p class="error" id="login-email-error" role="alert">A valid email is required.</p>
              }
            </div>
            <div class="field">
              <label for="login-password">Password</label>
              <input id="login-password" type="password" formControlName="password"
                     autocomplete="current-password" aria-required="true"
                     [attr.aria-invalid]="invalid('password') ? 'true' : null"
                     [attr.aria-describedby]="invalid('password') ? 'login-password-error' : null" />
              @if (invalid('password')) {
                <p class="error" id="login-password-error" role="alert">Password is required.</p>
              }
            </div>
            <button type="submit" [disabled]="busy()" [attr.aria-busy]="busy() ? 'true' : null">
              {{ busy() ? 'Signing in…' : 'Sign in' }}
            </button>
          </div>
          <div aria-live="assertive">
            @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          </div>
          <p class="hint">Demo writer — <code>demo&#64;example.com</code> / <code>P&#64;ssw0rd!</code></p>
        </form>
      }
    </section>
  `,
  styles: [`
    .auth-bar { padding: 0.75rem 1rem; background: #f1f3f5; border-radius: 6px; margin-bottom: 1.5rem; }
    .row { display: flex; gap: 1rem; align-items: flex-end; flex-wrap: wrap; }
    .field { display: flex; flex-direction: column; gap: 0.25rem; }
    label { font-weight: 600; font-size: 0.8rem; }
    input { font: inherit; padding: 0.4rem 0.5rem; border: 1px solid #adb5bd; border-radius: 6px; }
    input:focus { outline: 2px solid #0d6efd; outline-offset: 1px; }
    input[aria-invalid='true'] { border-color: #b02a37; }
    button { font: inherit; padding: 0.45rem 1rem; border: 0; border-radius: 6px;
             background: #198754; color: #fff; cursor: pointer; }
    button:disabled { background: #6c757d; cursor: progress; }
    .signed-in { margin: 0; display: flex; gap: 0.75rem; align-items: center; }
    .signed-in button { background: #6c757d; }
    .error { color: #b02a37; font-size: 0.8rem; margin: 0.25rem 0 0; }
    .hint { font-size: 0.75rem; color: #6c757d; margin: 0.4rem 0 0; }
  `],
})
export class AuthBarComponent {
  protected readonly auth = inject(AuthService);
  private   readonly fb   = inject(FormBuilder);

  protected readonly busy  = signal<boolean>(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

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
    } catch (err: unknown) {
      this.error.set(
        err instanceof HttpErrorResponse && err.status === 401
          ? 'Invalid email or password.'
          : 'Sign-in failed. Is the API running on :5075?',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
