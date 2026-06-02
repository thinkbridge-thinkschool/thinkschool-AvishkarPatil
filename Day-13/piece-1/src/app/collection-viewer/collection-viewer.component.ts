// ── Day-13 piece-1 — CollectionViewerComponent ───────────────────────────
//
// This is the exercise centrepiece.  It demonstrates every Day-13 signal
// primitive against the REAL Week-1 API:
//
//   signal()    — two writable signals: searchTerm and selectedAuthor
//   computed()  — ONE derived value from BOTH signals (filteredQuotes)
//   effect()    — side-effect that logs whenever the computed changes
//   @for        — renders the derived list, track by quote.id
//   @if         — loading / error / empty / loaded branches
//   @switch     — per-card author-tier badge
//   inject()    — no constructor; dependencies resolved via inject()
//
// Data comes from QuotesService, which calls GET /api/collections/{id} on the
// Week-1 API via httpResource (signals-first HTTP).  No fixtures.
//
// Standalone: no NgModule, no declarations array.
// ─────────────────────────────────────────────────────────────────────────

import {
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule }    from '@angular/forms';
import { QuotesService }  from '../quotes.service';
import { Quote }          from '../models/quote';

@Component({
  selector: 'app-collection-viewer',
  standalone: true,
  imports: [FormsModule],
  template: `
    <h1>Quote Collection</h1>
    <h2>Signals + Zoneless + Standalone · Day 13 Piece 1</h2>

    <!-- ── State machine: loading / error / loaded ──────────────────── -->
    <!-- @switch over the resource status drives which branch renders.
         This is how the edges (loading spinner, API failure) are exercised. -->
    @if (isLoading()) {
      <p class="stats">Loading collection from the Week-1 API…</p>
    } @else if (error()) {
      <div class="effect-log">
        Failed to load collection {{ collectionId() }} from the API.<br>
        Is the Week-1 QuotesApi running on http://localhost:5075 ?
      </div>
    } @else {

      <!-- Controls ───────────────────────────────────────────────── -->
      <div class="controls">
        <!-- Two-way binding to the searchTerm signal via ngModel.
             Every keystroke writes the signal, invalidating filteredQuotes. -->
        <input
          type="text"
          placeholder="Search text…"
          [ngModel]="searchTerm()"
          (ngModelChange)="searchTerm.set($event)"
        />

        <!-- selectedAuthor is the second signal driving filteredQuotes. -->
        <select
          [ngModel]="selectedAuthor()"
          (ngModelChange)="selectedAuthor.set($event)"
        >
          <option value="">All authors</option>
          @for (author of authors(); track author) {
            <option [value]="author">{{ author }}</option>
          }
        </select>

        <button (click)="resetFilters()">Reset</button>
      </div>

      <!-- Stats line — computed values used directly in the template ─ -->
      <p class="stats">
        {{ collectionName() }} — showing {{ filteredQuotes().length }} of {{ totalCount() }} quotes
        @if (searchTerm() || selectedAuthor()) {
          · filtered
        }
      </p>

      <!-- ── THE EXERCISE LIST ──────────────────────────────────────── -->
      <!--
        The new control-flow @for replaces *ngFor. The track expression is
        MANDATORY — Angular uses it to reconcile the DOM on re-render without
        destroying and recreating identical nodes.
      -->
      @if (filteredQuotes().length > 0) {
        <ul class="quote-list">
          @for (quote of filteredQuotes(); track quote.id) {
            <li class="quote-card">

              <!-- @switch replaces ngSwitch — author-tier badge. -->
              @switch (authorTier(quote.author)) {
                @case ('stoic') {
                  <span class="tag">Stoic</span>
                }
                @default {
                  <span class="tag">Philosopher</span>
                }
              }

              <p class="text">"{{ quote.text }}"</p>
              <p class="meta">— {{ quote.author }}</p>
            </li>
          }
        </ul>
      } @else {
        <p class="empty">No quotes match the current filters.</p>
      }

      <!-- Effect log — last time filteredQuotes changed ─────────────── -->
      @if (lastFilterChange()) {
        <div class="effect-log">
          effect() fired at {{ lastFilterChange() }}<br>
          → {{ filteredQuotes().length }} quote(s) in view
        </div>
      }
    }
  `,
})
export class CollectionViewerComponent {

  // ── inject() — no constructor, no constructor parameters ──────────
  private readonly quotesService = inject(QuotesService);

  // ── Resource state, surfaced from the service (all signals) ───────
  readonly isLoading      = this.quotesService.isLoading;
  readonly error          = this.quotesService.error;
  readonly collectionId   = this.quotesService.collectionId;
  readonly collectionName = this.quotesService.collectionName;

  // ── Signal 1: the free-text search term ───────────────────────────
  readonly searchTerm = signal<string>('');

  // ── Signal 2: the author filter ───────────────────────────────────
  readonly selectedAuthor = signal<string>('');

  // ── effect() log timestamp ────────────────────────────────────────
  readonly lastFilterChange = signal<string>('');

  // ── computed() — derived from BOTH signals + the API-backed list ──
  // filteredQuotes reads:
  //   • quotesService.quotes()   — the live list signal from the API
  //   • searchTerm()             — the text filter signal
  //   • selectedAuthor()         — the author filter signal
  // Any of the three changing re-evaluates this on next read and triggers
  // a single targeted re-render of the @for list.
  readonly filteredQuotes = computed<Quote[]>(() => {
    const term   = this.searchTerm().toLowerCase().trim();
    const author = this.selectedAuthor();

    return this.quotesService.quotes().filter(q => {
      const matchesText   = !term   || q.text.toLowerCase().includes(term)
                                    || q.author.toLowerCase().includes(term);
      const matchesAuthor = !author || q.author === author;
      return matchesText && matchesAuthor;
    });
  });

  // ── computed() — total count of the loaded collection ─────────────
  readonly totalCount = computed(() => this.quotesService.quotes().length);

  // ── computed() — unique author list for the dropdown ─────────────
  readonly authors = computed<string[]>(() => [
    ...new Set(this.quotesService.quotes().map(q => q.author)),
  ].sort());

  // ── effect() — runs whenever filteredQuotes changes ───────────────
  // Reads filteredQuotes() (registers the dependency) and writes a separate
  // signal (lastFilterChange) which filteredQuotes does NOT read — so there
  // is no circular dependency.
  private readonly filterEffect = effect(() => {
    const count = this.filteredQuotes().length;
    void count; // dependency registered; value intentionally unused
    this.lastFilterChange.set(new Date().toLocaleTimeString());
  });

  // ── Template helpers ──────────────────────────────────────────────
  resetFilters(): void {
    this.searchTerm.set('');
    this.selectedAuthor.set('');
  }

  // Pure function — no signal read, safe to call from @switch template.
  authorTier(author: string): 'stoic' | 'other' {
    return ['Marcus Aurelius', 'Seneca', 'Epictetus'].includes(author)
      ? 'stoic'
      : 'other';
  }
}
