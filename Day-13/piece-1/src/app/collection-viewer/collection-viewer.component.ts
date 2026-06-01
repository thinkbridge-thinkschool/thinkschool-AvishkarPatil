// ── Day-13 piece-1 — CollectionViewerComponent ───────────────────────────
//
// This is the exercise centrepiece.  It demonstrates every Day-13 signal
// primitive in one place:
//
//   signal()    — two writable signals: searchTerm and selectedAuthor
//   computed()  — ONE derived value from BOTH signals (filteredQuotes)
//   effect()    — side-effect that logs whenever the computed changes
//   @for        — renders the derived list, track by quote.id
//   @if         — conditional empty-state message
//   @switch     — per-card layout switch based on author tier
//   inject()    — no constructor; dependencies resolved via inject()
//
// Standalone: no NgModule, no declarations array.  All dependencies are
// listed in the component's own `imports` array.
// ─────────────────────────────────────────────────────────────────────────

import {
  Component,
  OnInit,
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
  // No NgModule required — imports live on the component itself.
  imports: [FormsModule],
  template: `
    <h1>Quote Collection</h1>
    <h2>Signals + Zoneless + Standalone · Day 13 Piece 1</h2>

    <!-- Controls ─────────────────────────────────────────────────── -->
    <div class="controls">
      <!-- Two-way binding to the searchTerm signal via ngModel
           (FormsModule imported above).  Every keystroke writes the signal,
           which immediately invalidates filteredQuotes. -->
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

    <!-- Stats line — computed values used directly in the template ── -->
    <p class="stats">
      Showing {{ filteredQuotes().length }} of {{ totalCount() }} quotes
      @if (searchTerm() || selectedAuthor()) {
        · filtered
      }
    </p>

    <!-- ── THE EXERCISE LIST ──────────────────────────────────────── -->
    <!--
      The new control-flow @for replaces *ngFor. The track expression is
      MANDATORY — Angular uses it to reconcile the DOM on re-render without
      destroying and recreating identical nodes. Without track, Angular would
      re-create every DOM node on every signal update.
    -->
    @if (filteredQuotes().length > 0) {
      <ul class="quote-list">
        @for (quote of filteredQuotes(); track quote.id) {
          <li class="quote-card">

            <!--
              @switch replaces ngSwitch.  Here it changes the author label
              style based on a computed "tier" — a small demo of @switch in a
              real-looking context rather than a toy example.
            -->
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

    <!-- Effect log — shows the last time filteredQuotes changed ──── -->
    @if (lastFilterChange()) {
      <div class="effect-log">
        effect() fired at {{ lastFilterChange() }}<br>
        → {{ filteredQuotes().length }} quote(s) in view
      </div>
    }
  `,
})
export class CollectionViewerComponent implements OnInit {

  // ── inject() — no constructor, no constructor parameters ──────────
  // inject() resolves the DI token at construction time, just like a
  // constructor parameter would, but it works in field initialisers and
  // in standalone functions outside a class.
  private readonly quotesService = inject(QuotesService);

  // ── Signal 1: the free-text search term ───────────────────────────
  // WritableSignal<string> — the template writes it via (ngModelChange).
  readonly searchTerm = signal<string>('');

  // ── Signal 2: the author filter ───────────────────────────────────
  // WritableSignal<string> — '' means "show all".
  readonly selectedAuthor = signal<string>('');

  // ── effect() log ─────────────────────────────────────────────────
  // Holds the last timestamp the filter effect fired.  Initialised to
  // empty so the log div is hidden until the first user interaction.
  readonly lastFilterChange = signal<string>('');

  // ── computed() — derived from BOTH signals ────────────────────────
  // filteredQuotes reads:
  //   • quotesService.quotes()   — the base list signal from the service
  //   • searchTerm()             — the text filter signal
  //   • selectedAuthor()         — the author filter signal
  //
  // Angular tracks every signal read inside a computed() call.  When any
  // of the three changes, filteredQuotes is lazily re-evaluated on its
  // next read.  The template reads it inside @for, so a signal write in
  // the controls section triggers a single targeted re-render of the list.
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

  // ── computed() — total count (no filter applied) ──────────────────
  readonly totalCount = computed(() => this.quotesService.quotes().length);

  // ── computed() — unique author list for the dropdown ─────────────
  readonly authors = computed<string[]>(() => [
    ...new Set(this.quotesService.quotes().map(q => q.author)),
  ].sort());

  // ── effect() — runs whenever filteredQuotes changes ───────────────
  // effect() is Angular's escape hatch for side effects that don't
  // belong in the view.  Common uses: logging, localStorage sync,
  // analytics events, external library updates.
  //
  // The effect body reads filteredQuotes() — that registers the signal
  // as a dependency.  Whenever filteredQuotes is re-evaluated (because
  // searchTerm or selectedAuthor changed), this effect re-runs.
  //
  // Note: effect() must be created in an injection context (constructor,
  // field initialiser, or inside inject()-enabled code).  The effect
  // runs once immediately on creation, then re-runs on dependency change.
  private readonly filterEffect = effect(() => {
    // Reading the signal registers it as a dependency.
    const count = this.filteredQuotes().length;
    // Write to a separate signal — avoids a circular dependency.
    // effect() bodies must not write to the signals they read without
    // wrapping the write in untracked() or a microtask.  Here we write to
    // lastFilterChange which filteredQuotes does NOT read, so it is safe.
    this.lastFilterChange.set(new Date().toLocaleTimeString());
  });

  ngOnInit(): void {
    // Nothing needed — signal state is initialised in field declarations.
    // OnInit is here only to show that lifecycle hooks still work as normal
    // in zoneless components.
  }

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
