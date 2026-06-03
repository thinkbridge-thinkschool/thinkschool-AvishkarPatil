// ── QuotesListComponent (list + detail + filtering) ───────────────────────
//
// A quotes LIST + DETAIL screen against the real Week-1 API:
//   list   → GET /api/quotes?page={page}&size={size}
//   detail → GET /api/quotes/{id}
//
// Primary feature (Day-13 piece-2): list + detail with independent
// loading/error/data signals, inject(), typed model, @if/@for/@switch, and a
// handled stale-response race on the detail fetch.
//
// Restored from Day-13 piece-1: client-side SEARCH + AUTHOR FILTER + RESET,
// built from the same signal/computed primitives — searchTerm + selectedAuthor
// signals feed a filteredQuotes computed; authors()/totalCount() are derived;
// resetFilters() clears both. Filtering applies to the currently-loaded page
// (quotes.quotes()), exactly as piece-1 filtered its loaded collection.
//
// Standalone — no NgModule.
// ─────────────────────────────────────────────────────────────────────────

import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule }    from '@angular/forms';
import { QuotesService }  from '../quotes.service';
import { Quote }          from '../models/quote';
import { AppError }       from '../models/app-error';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [FormsModule],
  template: `
    <h1>Quotes</h1>
    <h2>List + Detail · live Week-1 API</h2>

    <div class="layout">

      <!-- ── LIST PANE ──────────────────────────────────────────────── -->
      <section class="list-pane">
        <div class="controls">
          <button (click)="quotes.prevPage()" [disabled]="quotes.page() === 1">‹ Prev</button>
          <span>Page {{ quotes.page() }}</span>
          <button (click)="quotes.nextPage()">Next ›</button>
        </div>

        <!-- List state machine: loading / error / data -->
        @if (quotes.listLoading()) {
          <p class="status">Loading quotes…</p>
        } @else if (quotes.listError()) {
          <!-- Friendly message from the mapped AppError (errorInterceptor),
               so a 4xx shows the server's ProblemDetails detail rather than a
               misleading "is the API down?" string. -->
          <p class="status error">{{ listErrorMessage() }}</p>
        } @else {

          <!-- Filter controls (restored from piece-1) -->
          <div class="filters">
            <input
              type="text"
              placeholder="Search text or author…"
              aria-label="Search quotes"
              [ngModel]="searchTerm()"
              (ngModelChange)="searchTerm.set($event)"
            />
            <select
              aria-label="Filter by author"
              [ngModel]="selectedAuthor()"
              (ngModelChange)="selectedAuthor.set($event)"
            >
              <option value="">All authors</option>
              @for (author of authors(); track author) {
                <option [value]="author">{{ author }}</option>
              }
            </select>
            <button type="button" (click)="resetFilters()">Reset</button>
          </div>

          <!-- Counts — derived straight from the computeds -->
          <p class="stats">
            Showing {{ filteredQuotes().length }} of {{ totalCount() }} on this page
            @if (searchTerm() || selectedAuthor()) { · filtered }
          </p>

          @if (quotes.quotes().length === 0) {
            <p class="status empty">No quotes on this page.</p>
          } @else if (filteredQuotes().length === 0) {
            <p class="status empty">No quotes match the current filters.</p>
          } @else {
            <ul class="quote-list">
              <!-- @for over the FILTERED list, MANDATORY track quote.id -->
              @for (quote of filteredQuotes(); track quote.id) {
                <li
                  class="quote-row"
                  [class.selected]="quote.id === quotes.selectedId()"
                  (click)="quotes.selectQuote(quote.id)"
                >
                  <span class="row-author">{{ quote.author }}</span>
                  <span class="row-text">{{ quote.text }}</span>
                </li>
              }
            </ul>
          }
        }
      </section>

      <!-- ── DETAIL PANE ────────────────────────────────────────────── -->
      <section class="detail-pane">
        <!-- Detail state machine — independent of the list's state -->
        @switch (detailState()) {
          @case ('idle') {
            <p class="status">Select a quote to see its detail.</p>
          }
          @case ('loading') {
            <p class="status">Loading quote {{ quotes.selectedId() }}…</p>
          }
          @case ('error') {
            <p class="status error">{{ detailErrorMessage() }}</p>
          }
          @case ('loaded') {
            <article class="detail-card">
              <p class="detail-text">"{{ quotes.selectedQuote()!.text }}"</p>
              <p class="detail-author">— {{ quotes.selectedQuote()!.author }}</p>
              <dl class="detail-meta">
                <dt>id</dt>        <dd>{{ quotes.selectedQuote()!.id }}</dd>
                <dt>createdAt</dt> <dd>{{ quotes.selectedQuote()!.createdAt }}</dd>
                <dt>ownerId</dt>   <dd>{{ quotes.selectedQuote()!.ownerId ?? '—' }}</dd>
              </dl>
              <button (click)="quotes.clearSelection()">Close</button>
            </article>
          }
        }
      </section>

    </div>
  `,
  styles: [`
    .layout { display: flex; gap: 1.5rem; align-items: flex-start; }
    .list-pane { flex: 1 1 50%; }
    .detail-pane { flex: 1 1 50%; }
    .controls { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1rem; }
    .filters { display: flex; gap: 0.5rem; align-items: center; margin-bottom: 0.5rem; flex-wrap: wrap; }
    .filters input, .filters select {
      font: inherit; padding: 0.35rem 0.5rem; border: 1px solid #adb5bd; border-radius: 6px;
    }
    .filters input { flex: 1 1 12rem; }
    .filters input:focus, .filters select:focus { outline: 2px solid #0d6efd; outline-offset: 1px; }
    .stats { font-size: 0.8rem; color: #6c757d; margin: 0 0 0.75rem; }
    .quote-list { list-style: none; display: flex; flex-direction: column; gap: 0.4rem; }
    .quote-row {
      display: flex; flex-direction: column; gap: 0.2rem;
      padding: 0.6rem 0.8rem; background: #fff; border: 1px solid #dee2e6;
      border-radius: 6px; cursor: pointer;
    }
    .quote-row:hover { border-color: #0d6efd; }
    .quote-row.selected { border-color: #0d6efd; background: #e7f1ff; }
    .row-author { font-weight: 600; font-size: 0.85rem; }
    .row-text { font-size: 0.85rem; color: #495057;
                overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .detail-card { background: #fff; border: 1px solid #dee2e6; border-radius: 6px; padding: 1rem; }
    .detail-text { font-size: 1rem; margin-bottom: 0.5rem; }
    .detail-author { color: #6c757d; margin-bottom: 0.75rem; }
    .detail-meta { display: grid; grid-template-columns: auto 1fr; gap: 0.2rem 1rem; font-size: 0.8rem; }
    .detail-meta dt { color: #6c757d; }
    .status { color: #6c757d; padding: 0.75rem 0; }
    .status.error { color: #b02a37; }
    .status.empty { font-style: italic; }
  `],
})
export class QuotesListComponent {

  // inject() — no constructor parameter.
  protected readonly quotes = inject(QuotesService);

  // ── Filter signals (restored from Day-13 piece-1) ─────────────────
  protected readonly searchTerm     = signal<string>('');
  protected readonly selectedAuthor = signal<string>('');

  // ── Derived from the loaded page + the two filter signals ─────────
  // filteredQuotes re-evaluates whenever quotes(), searchTerm(), or
  // selectedAuthor() change — one targeted re-render of the @for list.
  protected readonly filteredQuotes = computed<Quote[]>(() => {
    const term   = this.searchTerm().toLowerCase().trim();
    const author = this.selectedAuthor();

    return this.quotes.quotes().filter(q => {
      const matchesText   = !term   || q.text.toLowerCase().includes(term)
                                    || q.author.toLowerCase().includes(term);
      const matchesAuthor = !author || q.author === author;
      return matchesText && matchesAuthor;
    });
  });

  // Total of the loaded page; the dropdown's unique, sorted author list.
  protected readonly totalCount = computed<number>(() => this.quotes.quotes().length);
  protected readonly authors    = computed<string[]>(() =>
    [...new Set(this.quotes.quotes().map(q => q.author))].sort());

  resetFilters(): void {
    this.searchTerm.set('');
    this.selectedAuthor.set('');
  }

  // ── Friendly error messages (mapped AppError from the errorInterceptor) ──
  // httpResource.error() is typed `unknown`; the errorInterceptor guarantees
  // it's an AppError at runtime, so read .message and fall back defensively.
  // This is what makes a 4xx surface the server's ProblemDetails detail in the
  // UI instead of a hardcoded "is the API down?" line.
  private friendly(err: unknown, fallback: string): string {
    return err instanceof AppError ? err.message : fallback;
  }

  protected readonly listErrorMessage = computed<string>(() =>
    this.friendly(this.quotes.listError(), 'Failed to load quotes. Is the Week-1 API running on :5075?'));

  protected readonly detailErrorMessage = computed<string>(() =>
    this.friendly(this.quotes.detailError(),
      `Couldn't load quote ${this.quotes.selectedId()}. It may have been deleted (404).`));

  // Detail state collapsed into one discriminant for the @switch.
  // Order matters: loading is checked before error/loaded so a re-fetch
  // shows the spinner rather than briefly flashing the previous error.
  protected detailState(): 'idle' | 'loading' | 'error' | 'loaded' {
    if (this.quotes.selectedId() === null) return 'idle';
    if (this.quotes.detailLoading())       return 'loading';
    if (this.quotes.detailError())         return 'error';
    if (this.quotes.selectedQuote())       return 'loaded';
    return 'idle';
  }
}
