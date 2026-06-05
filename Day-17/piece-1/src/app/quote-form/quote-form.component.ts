// ── Day-14 piece-1 — QuoteFormComponent (reactive create-a-quote form) ─────
//
// A reactive form that creates a quote against the real Week-1 API:
//   POST /api/quotes   body { author, text }   → 201 Created (Quote)
//
// Contract (QuotesApi/DTOs/CreateQuoteRequest.cs + Models/Quote.cs):
//   author : Required, MaxLength(200), not whitespace-only
//   text   : Required, MaxLength(1000), not whitespace-only
//   ownerId is taken from the JWT on the server — NOT a form field.
//
// Day-14 requirements covered:
//   • Reactive form (FormBuilder, typed nonNullable controls) + validators
//     that mirror the API's real limits (200 / 1000), plus a whitespace check
//     that matches the server's IsNullOrWhiteSpace guard.
//   • Error display gated on (touched || submitted) so a pristine form is quiet.
//   • Full a11y: <label for> ↔ id, aria-invalid + aria-describedby on errors,
//     role="alert" on field errors, aria-live for server-error and success,
//     keyboard-operable native controls, focus moved to the FIRST invalid
//     field on a failed submit.
//   • States: empty (pristine) / invalid / submitting (aria-busy) / server-error.
//
// Standalone — no NgModule.
// ─────────────────────────────────────────────────────────────────────────

import { Component, ElementRef, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { QuotesService } from '../quotes.service';

// Matches the server's string.IsNullOrWhiteSpace guard: Validators.required
// treats "   " as VALID (it's non-empty), but the API rejects it with 400.
// This validator closes that gap so the client and server agree.
function notBlank(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (typeof value === 'string' && value.length > 0 && value.trim().length === 0) {
    return { whitespace: true };
  }
  return null;
}

type FieldName = 'author' | 'text';

@Component({
  selector: 'app-quote-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <section class="form-pane">
      <h2 id="form-heading">Add a quote</h2>

      <!-- novalidate: we own validation + a11y; aria-labelledby names the form -->
      <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate aria-labelledby="form-heading">

        <!-- ── AUTHOR ───────────────────────────────────────────────── -->
        <div class="field">
          <label for="author">Author <span class="req" aria-hidden="true">*</span></label>
          <input
            id="author"
            type="text"
            formControlName="author"
            maxlength="200"
            autocomplete="off"
            [attr.aria-invalid]="showError('author') ? 'true' : null"
            [attr.aria-describedby]="describedBy('author')"
          />
          <p class="hint" id="author-hint">Required · up to 200 characters.</p>
          @if (showError('author')) {
            <p class="error" id="author-error" role="alert">
              @if (f.author.errors?.['required']) {
                Author is required.
              } @else if (f.author.errors?.['whitespace']) {
                Author can’t be only spaces.
              } @else if (f.author.errors?.['maxlength']) {
                Author must be 200 characters or fewer.
              }
            </p>
          }
        </div>

        <!-- ── TEXT ─────────────────────────────────────────────────── -->
        <div class="field">
          <label for="text">Quote text <span class="req" aria-hidden="true">*</span></label>
          <textarea
            id="text"
            rows="4"
            formControlName="text"
            maxlength="1000"
            [attr.aria-invalid]="showError('text') ? 'true' : null"
            [attr.aria-describedby]="describedBy('text')"
          ></textarea>
          <p class="hint" id="text-hint">
            Required · {{ f.text.value.length }}/1000 characters.
          </p>
          @if (showError('text')) {
            <p class="error" id="text-error" role="alert">
              @if (f.text.errors?.['required']) {
                Quote text is required.
              } @else if (f.text.errors?.['whitespace']) {
                Quote text can’t be only spaces.
              } @else if (f.text.errors?.['maxlength']) {
                Quote text must be 1000 characters or fewer.
              }
            </p>
          }
        </div>

        <!-- ── SERVER ERROR (assertive: it interrupts) ──────────────── -->
        <div class="server-status" aria-live="assertive">
          @if (quotes.submitError()) {
            <p class="error server-error" role="alert">{{ quotes.submitError() }}</p>
          }
        </div>

        <button type="submit" [disabled]="quotes.submitting()"
                [attr.aria-busy]="quotes.submitting() ? 'true' : null">
          {{ quotes.submitting() ? 'Adding…' : 'Add quote' }}
        </button>
      </form>

      <!-- ── SUCCESS (polite: announced after, doesn't interrupt) ───── -->
      <div class="success-status" aria-live="polite">
        @if (quotes.lastCreated(); as created) {
          <p class="success" role="status">
            Added quote #{{ created.id }} by {{ created.author }}.
          </p>
        }
      </div>
    </section>
  `,
  styles: [`
    .form-pane { max-width: 640px; margin-bottom: 2rem; }
    .field { display: flex; flex-direction: column; gap: 0.3rem; margin-bottom: 1rem; }
    label { font-weight: 600; font-size: 0.9rem; }
    .req { color: #b02a37; }
    input, textarea {
      font: inherit; padding: 0.5rem 0.6rem;
      border: 1px solid #adb5bd; border-radius: 6px;
    }
    input:focus, textarea:focus { outline: 2px solid #0d6efd; outline-offset: 1px; }
    input[aria-invalid='true'], textarea[aria-invalid='true'] { border-color: #b02a37; }
    .hint { font-size: 0.78rem; color: #565d64; margin: 0; }
    .error { font-size: 0.8rem; color: #b02a37; margin: 0; }
    .server-error { font-weight: 600; }
    .success { font-size: 0.85rem; color: #0f5132; font-weight: 600; }
    button {
      font: inherit; padding: 0.5rem 1.1rem; border: 0; border-radius: 6px;
      background: #0d6efd; color: #fff; cursor: pointer;
    }
    button:disabled { background: #565d64; cursor: progress; }
    button:focus-visible { outline: 2px solid #08306b; outline-offset: 2px; }
  `],
})
export class QuoteFormComponent {
  protected readonly quotes = inject(QuotesService);
  private readonly fb       = inject(FormBuilder);
  private readonly host     = inject<ElementRef<HTMLElement>>(ElementRef);

  // submitted flips true on the first submit attempt so errors appear for
  // fields the user never touched. It's a signal → setting it triggers
  // change detection under zoneless (reactive-form state changes alone don't).
  protected readonly submitted = signal<boolean>(false);

  // nonNullable → controls are string (never null); getRawValue() is typed,
  // so no `any` and no non-null assertions downstream.
  protected readonly form = this.fb.nonNullable.group({
    author: ['', [Validators.required, Validators.maxLength(200), notBlank]],
    text:   ['', [Validators.required, Validators.maxLength(1000), notBlank]],
  });

  protected get f() {
    return this.form.controls;
  }

  // Show a field's error once it's been touched OR a submit has been attempted.
  protected showError(name: FieldName): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || this.submitted());
  }

  // Always link the hint; add the error id only while the error is visible,
  // so a screen reader reads the hint normally and the error when it appears.
  protected describedBy(name: FieldName): string {
    return this.showError(name) ? `${name}-hint ${name}-error` : `${name}-hint`;
  }

  protected async onSubmit(): Promise<void> {
    this.submitted.set(true);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    const { author, text } = this.form.getRawValue();
    try {
      await this.quotes.createQuote({ author: author.trim(), text: text.trim() });
      this.form.reset();        // back to empty/pristine on success
      this.submitted.set(false);
    } catch {
      // Server message is already in quotes.submitError(); keep the user's
      // input so they can retry without retyping.
    }
  }

  // Move keyboard focus to the first invalid control (by stable id, in field
  // order) so a submit with errors lands the user on the problem, not the top.
  private focusFirstInvalid(): void {
    const order: FieldName[] = ['author', 'text'];
    const firstInvalid = order.find(name => this.form.controls[name].invalid);
    if (firstInvalid) {
      this.host.nativeElement
        .querySelector<HTMLElement>(`#${firstInvalid}`)
        ?.focus();
    }
  }
}
