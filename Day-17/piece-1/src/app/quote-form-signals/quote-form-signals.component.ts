// ── Day-14 piece-2 — QuoteFormSignalsComponent (Signal Forms preview) ──────
//
// The SAME create-a-quote form as piece-1, rebuilt with Angular's experimental
// Signal Forms preview API (@angular/forms/signals, Angular 21.2).
//
//   POST /api/quotes   body { author, text }   → 201 Created (Quote)
//
// Contract (QuotesApi/DTOs/CreateQuoteRequest.cs + Models/Quote.cs):
//   author : Required, MaxLength(200), not whitespace-only
//   text   : Required, MaxLength(1000), not whitespace-only
//   ownerId is taken from the JWT on the server — NOT a form field.
//
// Signal Forms shape:
//   • a plain signal() model holds the data
//   • form(model, schema) builds a reactive FieldTree
//   • validators (required / maxLength / validate) are declared in the schema,
//     keyed by field PATH — not attached to controls
//   • [formField] binds a native control to a field AND auto-syncs the native
//     `required` / `maxlength` / `disabled` props from the schema (verified in
//     the bundle: nativeControl writes required/maxLength/disabled/readonly)
//   • submit(form, {action}) marks all touched, runs the action only if valid,
//     manages submitting(), and folds returned server errors back into the form
//
// What Signal Forms does NOT do (so we still hand-wire it, same as piece-1):
//   • aria-invalid / aria-describedby — the directive manages no ARIA at all
//   • focus-to-first-error on submit — done manually via the onInvalid hook
// ─────────────────────────────────────────────────────────────────────────

import { Component, ElementRef, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  form, submit, required, maxLength, validate, FormField, ValidationError,
} from '@angular/forms/signals';
import { QuotesService } from '../quotes.service';
import { Quote }         from '../models/quote';

type FieldName = 'author' | 'text';

// Custom validator mirroring the server's IsNullOrWhiteSpace guard — the
// built-in required() treats "   " as present, but the API rejects it (400).
function notBlank(value: string, label: string): ValidationError | null {
  return typeof value === 'string' && value.length > 0 && value.trim().length === 0
    ? { kind: 'whitespace', message: `${label} can’t be only spaces.` }
    : null;
}

@Component({
  selector: 'app-quote-form-signals',
  standalone: true,
  imports: [FormField],
  template: `
    <section class="form-pane">
      <h2 id="sf-heading">Add a quote <span class="badge">Signal Forms</span></h2>

      <!-- Plain native form; Signal Forms binds per-control via [formField]. -->
      <form (submit)="$event.preventDefault(); onSubmit()" novalidate aria-labelledby="sf-heading">

        <!-- ── AUTHOR ───────────────────────────────────────────────── -->
        <div class="field">
          <label for="author">Author <span class="req" aria-hidden="true">*</span></label>
          <!-- No maxlength/required here: [formField] derives them from the schema. -->
          <input id="author" type="text" autocomplete="off"
                 [formField]="form.author"
                 [attr.aria-invalid]="showError('author') ? 'true' : null"
                 [attr.aria-describedby]="describedBy('author')" />
          <p class="hint" id="author-hint">Required · up to 200 characters.</p>
          @if (showError('author')) {
            <p class="error" id="author-error" role="alert">{{ firstError('author') }}</p>
          }
        </div>

        <!-- ── TEXT ─────────────────────────────────────────────────── -->
        <div class="field">
          <label for="text">Quote text <span class="req" aria-hidden="true">*</span></label>
          <textarea id="text" rows="4"
                    [formField]="form.text"
                    [attr.aria-invalid]="showError('text') ? 'true' : null"
                    [attr.aria-describedby]="describedBy('text')"></textarea>
          <!-- Counter is informational only: [formField] reflects the schema's
               maxLength onto the native control as maxlength="1000", so the
               value is hard-capped at 1000 and can never go over. -->
          <p class="hint" id="text-hint">
            Required · {{ form.text().value().length }}/1000 characters.
          </p>
          @if (showError('text')) {
            <p class="error" id="text-error" role="alert">{{ firstError('text') }}</p>
          }
        </div>

        <!-- ── SERVER ERROR ─────────────────────────────────────────── -->
        <!-- submit()'s action returns server errors; they land on the root
             form, so we read them from form().errors() filtered to kind. -->
        <div class="server-status" aria-live="assertive">
          @if (serverError(); as msg) {
            <p class="error server-error">{{ msg }}</p>
          }
        </div>

        <button type="submit" [disabled]="form().submitting()"
                [attr.aria-busy]="form().submitting() ? 'true' : null">
          {{ form().submitting() ? 'Adding…' : 'Add quote' }}
        </button>
      </form>

      <!-- ── SUCCESS ──────────────────────────────────────────────── -->
      <div class="success-status" aria-live="polite">
        @if (lastCreated(); as created) {
          <p class="success">Added quote #{{ created.id }} by {{ created.author }}.</p>
        }
      </div>
    </section>
  `,
  styles: [`
    .form-pane { max-width: 640px; margin-bottom: 2rem; }
    .badge { font-size: 0.65rem; background: #6f42c1; color: #fff; padding: 0.1rem 0.4rem;
             border-radius: 4px; vertical-align: middle; }
    .field { display: flex; flex-direction: column; gap: 0.3rem; margin-bottom: 1rem; }
    label { font-weight: 600; font-size: 0.9rem; }
    .req { color: #b02a37; }
    input, textarea { font: inherit; padding: 0.5rem 0.6rem; border: 1px solid #adb5bd; border-radius: 6px; }
    input:focus, textarea:focus { outline: 2px solid #0d6efd; outline-offset: 1px; }
    input[aria-invalid='true'], textarea[aria-invalid='true'] { border-color: #b02a37; }
    .hint { font-size: 0.78rem; color: #565d64; margin: 0; }
    .error { font-size: 0.8rem; color: #b02a37; margin: 0; }
    .server-error { font-weight: 600; }
    .success { font-size: 0.85rem; color: #0f5132; font-weight: 600; }
    button { font: inherit; padding: 0.5rem 1.1rem; border: 0; border-radius: 6px;
             background: #0d6efd; color: #fff; cursor: pointer; }
    button:disabled { background: #565d64; cursor: progress; }
    button:focus-visible { outline: 2px solid #08306b; outline-offset: 2px; }
  `],
})
export class QuoteFormSignalsComponent {
  private readonly quotes = inject(QuotesService);
  private readonly host   = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly router = inject(Router);

  // The data model is just a signal. Validators live in the schema below.
  private readonly model = signal({ author: '', text: '' });

  readonly form = form(this.model, (path) => {
    required (path.author, { message: 'Author is required.' });
    maxLength(path.author, 200, { message: 'Author must be 200 characters or fewer.' });
    validate (path.author, (ctx) => notBlank(ctx.value(), 'Author'));

    required (path.text, { message: 'Quote text is required.' });
    maxLength(path.text, 1000, { message: 'Quote text must be 1000 characters or fewer.' });
    validate (path.text, (ctx) => notBlank(ctx.value(), 'Quote text'));
  });

  readonly lastCreated = signal<Quote | null>(null);
  readonly serverError = signal<string | null>(null);

  // touched() and invalid() are signals, so this updates under zoneless with
  // no separate `submitted` flag (piece-1 needed one). submit() marks all
  // touched, so errors also appear after a blank submit.
  protected showError(name: FieldName): boolean {
    const state = this.form[name]();
    return state.touched() && state.invalid();
  }

  protected firstError(name: FieldName): string {
    return this.form[name]().errors()[0]?.message ?? '';
  }

  protected describedBy(name: FieldName): string {
    return this.showError(name) ? `${name}-hint ${name}-error` : `${name}-hint`;
  }

  protected async onSubmit(): Promise<void> {
    this.serverError.set(null);

    const ok = await submit(this.form, {
      // Runs ONLY if the form is valid. submit() manages submitting() for us.
      action: async (f) => {
        const { author, text } = f().value();
        try {
          // createQuote drives the signal-store optimistic path: the new row is
          // prepended to the store's optimisticQuotes overlay immediately, then
          // reconciled (success) or rolled back (failure). postQuote (the old
          // bare POST) is gone — there is now a single create write-path.
          const created = await this.quotes.createQuote({ author: author.trim(), text: text.trim() });
          this.lastCreated.set(created);
          return undefined; // success → no errors
        } catch (err: unknown) {
          // Returned errors fold back into the form; surface the message too.
          const message = this.quotes.describeError(err);
          this.serverError.set(message);
          return [{ kind: 'server', message }];
        }
      },
      // Invalid submit: Signal Forms doesn't move focus, so we do it.
      onInvalid: () => this.focusFirstInvalid(),
    });

    if (ok) {
      this.model.set({ author: '', text: '' }); // clear values
      this.form().reset();                       // clear touched/dirty/submitted
      // The repository orders newest-first, so the just-created quote lands on
      // top of page 1 — navigate back to the list to see it.
      void this.router.navigate(['/quotes']);
    }
  }

  private focusFirstInvalid(): void {
    const order: FieldName[] = ['author', 'text'];
    const first = order.find(name => this.form[name]().invalid());
    if (first) {
      this.host.nativeElement.querySelector<HTMLElement>(`#${first}`)?.focus();
    }
  }
}
