// ── QuoteDetailComponent — lazy route /quotes/:id ──────────────────────────
//
// Loaded on demand (loadComponent in app.routes.ts) when the user opens a quote.
// Reads the :id route param via withComponentInputBinding() → the `id` input,
// validates it, and drives the existing detail fetch on QuotesService:
//   GET /api/quotes/{id} → Quote (200) or 404
// :id maps to the real Quote.id (int) the API returns.
//
// States: invalid-id (non-numeric param, no request fired) / loading / error
// (404 or other, friendly AppError.message) / loaded. The detail card carries a
// view-transition-name so the list→detail navigation animates (withViewTransitions).

import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink }      from '@angular/router';
import { QuotesService }   from '../quotes.service';
import { AppError }        from '../models/app-error';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [RouterLink],
  template: `
    <a class="back" routerLink="/quotes">‹ Back to list</a>

    @if (invalidId()) {
      <p class="status error">“{{ id() }}” is not a valid quote id.</p>
    } @else {
      @switch (state()) {
        @case ('loading') {
          <p class="status">Loading quote {{ parsedId() }}…</p>
        }
        @case ('error') {
          <p class="status error">{{ errorMessage() }}</p>
        }
        @case ('loaded') {
          <article class="detail-card">
            <p class="detail-text">"{{ quote()!.text }}"</p>
            <p class="detail-author">— {{ quote()!.author }}</p>
            <dl class="detail-meta">
              <dt>id</dt>        <dd>{{ quote()!.id }}</dd>
              <dt>createdAt</dt> <dd>{{ quote()!.createdAt }}</dd>
              <dt>ownerId</dt>   <dd>{{ quote()!.ownerId ?? '—' }}</dd>
            </dl>
          </article>
        }
      }
    }
  `,
  styles: [`
    .back { display: inline-block; margin-bottom: 1rem; color: #0d6efd; text-decoration: none; }
    .back:hover { text-decoration: underline; }
    .status { color: #6c757d; padding: 0.75rem 0; }
    .status.error { color: #b02a37; }
    .detail-card {
      background: #fff; border: 1px solid #dee2e6; border-radius: 6px; padding: 1.25rem;
      max-width: 640px;
      /* Animate this card across the list→detail navigation. */
      view-transition-name: quote-detail-card;
    }
    .detail-text { font-size: 1.1rem; margin-bottom: 0.5rem; }
    .detail-author { color: #6c757d; margin-bottom: 0.75rem; }
    .detail-meta { display: grid; grid-template-columns: auto 1fr; gap: 0.2rem 1rem; font-size: 0.8rem; }
    .detail-meta dt { color: #6c757d; }
  `],
})
export class QuoteDetailComponent {
  private readonly quotes = inject(QuotesService);

  // Bound from the :id route param by withComponentInputBinding(). Route params
  // are always strings, so we parse and validate before issuing a request.
  readonly id = input.required<string>();

  // Valid Quote.id is a positive integer; anything else (e.g. /quotes/abc) is
  // a bad param we reject WITHOUT hitting the API.
  protected readonly parsedId = computed<number | null>(() => {
    const n = Number(this.id());
    return Number.isInteger(n) && n > 0 ? n : null;
  });
  protected readonly invalidId = computed<boolean>(() => this.parsedId() === null);

  protected readonly quote   = this.quotes.selectedQuote;
  protected readonly loading = this.quotes.detailLoading;

  protected readonly errorMessage = computed<string>(() => {
    const e = this.quotes.detailError();
    return e instanceof AppError ? e.message : 'Could not load this quote.';
  });

  constructor() {
    // When the param changes (incl. detail→detail navigation), point the
    // service's detail fetch at the new id. The service aborts any in-flight
    // request, so the stale-response race is already handled there.
    effect(() => {
      const n = this.parsedId();
      if (n !== null) {
        this.quotes.selectQuote(n);
      }
    });
  }

  // loading is checked first so a re-fetch shows the spinner instead of briefly
  // flashing the previous quote or a stale error.
  protected state(): 'loading' | 'error' | 'loaded' {
    if (this.loading())              return 'loading';
    if (this.quotes.detailError())   return 'error';
    if (this.quote())                return 'loaded';
    return 'loading';
  }
}
