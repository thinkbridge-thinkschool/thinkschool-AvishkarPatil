// ── Day-13 piece-2 — QuotesListComponent (list + detail) ──────────────────
//
// A quotes LIST + DETAIL screen against the real Week-1 API:
//   list   → GET /api/quotes?page={page}&size={size}
//   detail → GET /api/quotes/{id}
//
// Demonstrates the Day-13 requirements:
//   • signals for loading / error / data — independently for list AND detail
//   • inject() for the service (no constructor)
//   • fully typed model (Quote) — no `any`
//   • new control flow @if / @for (with track) / @switch
//   • stale-response race handled (rapid detail clicks abort the prior fetch)
//
// Standalone — no NgModule.
// ─────────────────────────────────────────────────────────────────────────

import { Component, inject } from '@angular/core';
import { QuotesService }     from '../quotes.service';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  template: `
    <h1>Quotes</h1>
    <h2>List + Detail · Day 13 Piece 2 · live Week-1 API</h2>

    <div class="layout">

      <!-- ── LIST PANE ──────────────────────────────────────────────── -->
      <section class="list-pane">
        <div class="controls">
          <button (click)="quotes.prevPage()" [disabled]="quotes.page() === 1">‹ Prev</button>
          <span>Page {{ quotes.page() }}</span>
          <button (click)="quotes.nextPage()">Next ›</button>
        </div>

        <!-- List state machine: loading / error / empty / data -->
        @if (quotes.listLoading()) {
          <p class="status">Loading quotes…</p>
        } @else if (quotes.listError()) {
          <p class="status error">
            Failed to load quotes. Is the Week-1 API running on :5075?
          </p>
        } @else if (quotes.quotes().length === 0) {
          <p class="status empty">No quotes on this page.</p>
        } @else {
          <ul class="quote-list">
            <!-- @for with MANDATORY track quote.id -->
            @for (quote of quotes.quotes(); track quote.id) {
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
            <p class="status error">
              Couldn't load quote {{ quotes.selectedId() }}. It may have been deleted (404).
            </p>
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
