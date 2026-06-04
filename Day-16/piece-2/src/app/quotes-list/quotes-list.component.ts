// ── QuotesListComponent — lazy route /quotes (list + filtering) ────────────
//
// LIST only against the real Week-1 API:
//   list → GET /api/quotes?page={page}&size={size}
//
// Day-16 change: the detail pane moved to its OWN lazy route (/quotes/:id,
// QuoteDetailComponent). Each row is now a routerLink to that route, so opening
// a quote triggers a navigation — which lazy-loads the detail chunk and runs a
// View Transition (withViewTransitions).
//
// Carried forward: client-side SEARCH + AUTHOR FILTER + RESET (signals/computed)
// and the friendly listErrorMessage() (mapped AppError from the errorInterceptor).
//
// Standalone — no NgModule.
// ─────────────────────────────────────────────────────────────────────────

import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule }    from '@angular/forms';
import { RouterLink }     from '@angular/router';
import { QuotesService }  from '../quotes.service';
import { Quote }          from '../models/quote';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <h1>Quotes</h1>
    <h2>List · live Week-1 API · routed</h2>

    <section class="list-pane">
      <div class="controls">
        <button (click)="quotes.prevPage()" [disabled]="quotes.page() === 1">‹ Prev</button>
        <span>Page {{ quotes.page() }}</span>
        <button (click)="quotes.nextPage()">Next ›</button>
      </div>

      <!-- Day-16 piece-2: the store models the screen state as a single
           discriminated union (ViewState). The template @switch-es on
           listView().status — one closed set of branches, no nested
           loading/error/empty bookkeeping in the component. -->
      @switch (quotes.listView().status) {

        @case ('loading') {
          <p class="status">Loading quotes…</p>
        }

        @case ('error') {
          <!-- message is part of the error variant of ViewState — already a
               friendly mapped AppError.message from the errorInterceptor. -->
          <p class="status error">{{ errorMessage() }}</p>
        }

        @case ('empty') {
          <p class="status empty">No quotes on this page.</p>
        }

        @case ('loaded') {
          <!-- Filter controls -->
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

          <p class="stats">
            Showing {{ filteredQuotes().length }} of {{ totalCount() }} on this page
            @if (searchTerm() || selectedAuthor()) { · filtered }
          </p>

          @if (filteredQuotes().length === 0) {
            <p class="status empty">No quotes match the current filters.</p>
          } @else {
            <ul class="quote-list">
              <!-- @for over the FILTERED list, MANDATORY track quote.id.
                   Each row routerLinks to the lazy detail route by Quote.id. -->
              @for (quote of filteredQuotes(); track quote.id) {
                <li>
                  <a class="quote-row" [routerLink]="['/quotes', quote.id]">
                    <span class="row-author">{{ quote.author }}</span>
                    <span class="row-text">{{ quote.text }}</span>
                  </a>
                </li>
              }
            </ul>
          }
        }
      }
    </section>
  `,
  styles: [`
    .list-pane { max-width: 720px; }
    .controls { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1rem; }
    .filters { display: flex; gap: 0.5rem; align-items: center; margin-bottom: 0.5rem; flex-wrap: wrap; }
    .filters input, .filters select {
      font: inherit; padding: 0.35rem 0.5rem; border: 1px solid #adb5bd; border-radius: 6px;
    }
    .filters input { flex: 1 1 12rem; }
    .filters input:focus, .filters select:focus { outline: 2px solid #0d6efd; outline-offset: 1px; }
    .stats { font-size: 0.8rem; color: #6c757d; margin: 0 0 0.75rem; }
    .quote-list { list-style: none; display: flex; flex-direction: column; gap: 0.4rem; padding: 0; }
    .quote-row {
      display: flex; flex-direction: column; gap: 0.2rem;
      padding: 0.6rem 0.8rem; background: #fff; border: 1px solid #dee2e6;
      border-radius: 6px; cursor: pointer; text-decoration: none; color: inherit;
    }
    .quote-row:hover { border-color: #0d6efd; }
    .row-author { font-weight: 600; font-size: 0.85rem; }
    .row-text { font-size: 0.85rem; color: #495057;
                overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .status { color: #6c757d; padding: 0.75rem 0; }
    .status.error { color: #b02a37; }
    .status.empty { font-style: italic; }
  `],
})
export class QuotesListComponent {

  protected readonly quotes = inject(QuotesService);

  // ── Filter signals ────────────────────────────────────────────────
  protected readonly searchTerm     = signal<string>('');
  protected readonly selectedAuthor = signal<string>('');

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

  protected readonly totalCount = computed<number>(() => this.quotes.quotes().length);
  protected readonly authors    = computed<string[]>(() =>
    [...new Set(this.quotes.quotes().map(q => q.author))].sort());

  resetFilters(): void {
    this.searchTerm.set('');
    this.selectedAuthor.set('');
  }

  // The error message now lives on the store's ViewState (error variant), so
  // the component just unwraps it — no AppError handling in the component.
  protected readonly errorMessage = computed<string>(() => {
    const view = this.quotes.listView();
    return view.status === 'error' ? view.message : '';
  });
}
