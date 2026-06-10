// ── QuotesListComponent — lazy route /quotes (list + filtering + sorting) ──
//
// LIST only against the real Week-1 API:
//   list → GET /api/quotes?page={page}&size={size}
//
// Sorting, filtering, and date formatting all happen client-side on the
// current page. The API returns no total-count envelope; pagination metadata
// is inferred via the watermark pattern in QuotesService.
//
// Standalone — no NgModule.
// ─────────────────────────────────────────────────────────────────────────

import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule }    from '@angular/forms';
import { RouterLink }     from '@angular/router';
import { QuotesService }  from '../quotes.service';
import { AuthService }    from '../auth.service';
import { Quote }          from '../models/quote';

type SortOption = 'default' | 'author-asc' | 'author-desc' | 'newest' | 'oldest';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="page-header">
      <h1>Quotes</h1>
      @if (auth.isAuthenticated()) {
        <a class="btn-new-quote" routerLink="/quotes/new">+ New Quote</a>
      } @else {
        <span class="btn-new-quote btn-new-quote--locked"
              role="button"
              aria-disabled="true"
              tabindex="0"
              title="Sign in to create quotes">
          🔒 New Quote
        </span>
      }
    </div>
    <h2>List · live Week-1 API · routed</h2>

    <!-- ── Stats dashboard (icons + hover animation) ─────────────── -->
    @if (viewStatus() === 'loaded') {
      <div class="stats-grid" role="region" aria-label="Page summary">
        <div class="stat-card">
          <span class="stat-icon" aria-hidden="true">📄</span>
          <span class="stat-value">{{ totalCount() }}</span>
          <span class="stat-label">Quotes on Page</span>
        </div>
        <div class="stat-card">
          <span class="stat-icon" aria-hidden="true">👤</span>
          <span class="stat-value">{{ statsAuthors() }}</span>
          <span class="stat-label">Authors</span>
        </div>
        <div class="stat-card">
          <span class="stat-icon" aria-hidden="true">📑</span>
          <span class="stat-value">
            {{ quotes.page() }}
            @if (quotes.totalPages() !== null) {
              <small class="stat-of">/ {{ quotes.totalPages() }}</small>
            }
          </span>
          <span class="stat-label">Current Page</span>
        </div>
      </div>
    }

    <section class="list-pane">
      <!-- ── Pagination row (always visible) ──────────────────────── -->
      <div class="controls">
        <div class="page-nav" role="navigation" aria-label="Pagination">
          <button class="page-btn page-btn--nav"
                  (click)="quotes.prevPage()"
                  [disabled]="quotes.page() === 1"
                  aria-label="Previous page">‹</button>

          @for (p of pageWindow(); track $index) {
            @if (isGap(p)) {
              <span class="page-ellipsis" aria-hidden="true">…</span>
            } @else {
              <button class="page-btn"
                      [class.page-btn--active]="p === quotes.page()"
                      (click)="quotes.goToPage(asNum(p))"
                      [attr.aria-current]="p === quotes.page() ? 'page' : null"
                      [attr.aria-label]="'Page ' + p">{{ p }}</button>
            }
          }

          <button class="page-btn page-btn--nav"
                  (click)="quotes.nextPage()"
                  [disabled]="quotes.isLastPage()"
                  aria-label="Next page">›</button>
        </div>

        <div class="page-size-row">
          <label for="page-size-select" class="size-lbl">Show</label>
          <select id="page-size-select" class="size-select"
                  [ngModel]="quotes.size()" (ngModelChange)="quotes.setSize(+$event)"
                  aria-label="Items per page">
            <option [ngValue]="5">5</option>
            <option [ngValue]="10">10</option>
            <option [ngValue]="20">20</option>
            <option [ngValue]="50">50</option>
          </select>
          <span class="size-lbl">per page</span>
        </div>
      </div>

      @switch (quotes.listView().status) {

        @case ('loading') {
          <div class="skeleton-list" aria-busy="true" aria-label="Loading quotes">
            @for (i of skeletonItems; track i) {
              <div class="skeleton-card">
                <div class="skel skel-avatar"></div>
                <div class="skel-body">
                  <div class="skel skel-author"></div>
                  <div class="skel skel-text"></div>
                </div>
              </div>
            }
          </div>
        }

        @case ('error') {
          <p class="status-msg status-msg--error" role="alert">{{ errorMessage() }}</p>
        }

        @case ('empty') {
          <div class="empty-state">
            <span class="empty-icon" aria-hidden="true">📭</span>
            <p class="empty-title">No quotes on this page</p>
            <p class="empty-sub">Try going back to page 1 or reducing the page size.</p>
          </div>
        }

        @case ('loaded') {
          <!-- Sticky filter + sort bar -->
          <div class="sticky-filters-wrap">
            <div class="filters">
              <!-- Search with inline icon -->
              <div class="search-wrapper">
                <span class="search-icon" aria-hidden="true">🔍</span>
                <input type="text"
                       placeholder="Search quotes or authors…"
                       aria-label="Search quotes"
                       [ngModel]="searchTerm()"
                       (ngModelChange)="searchTerm.set($event)" />
              </div>

              <!-- Author filter -->
              <select class="filter-select"
                      aria-label="Filter by author"
                      [ngModel]="selectedAuthor()"
                      (ngModelChange)="selectedAuthor.set($event)">
                <option value="">All authors</option>
                @for (author of authors(); track author) {
                  <option [value]="author">{{ author }}</option>
                }
              </select>

              <!-- Sort -->
              <select class="filter-select sort-select"
                      aria-label="Sort quotes"
                      [ngModel]="sortBy()"
                      (ngModelChange)="sortBy.set($event)">
                <option value="default">Sort: Default</option>
                <option value="author-asc">Author A–Z</option>
                <option value="author-desc">Author Z–A</option>
                <option value="newest">Newest first</option>
                <option value="oldest">Oldest first</option>
              </select>

              <button type="button" class="filter-reset" (click)="resetFilters()">Reset</button>
            </div>
          </div>

          <!-- Range / stats line -->
          <p class="range-line" aria-live="polite" aria-atomic="true">
            @if (searchTerm() || selectedAuthor()) {
              {{ filteredQuotes().length }} of {{ totalCount() }} match · filtered
            } @else if (quotes.estimatedTotal() !== null) {
              Showing {{ quotes.firstItem() }}–{{ quotes.lastItem() }} of ~{{ quotes.estimatedTotal() }} quotes
            } @else {
              Showing {{ quotes.firstItem() }}–{{ quotes.lastItem() }}
              @if (quotes.isLastPage()) { · last page }
            }
          </p>

          @if (filteredQuotes().length === 0) {
            <!-- Filter / search empty state -->
            <div class="empty-state">
              <span class="empty-icon" aria-hidden="true">🔍</span>
              <p class="empty-title">No quotes found</p>
              <p class="empty-sub">Try changing the search text or author filter.</p>
              <button type="button" class="empty-reset" (click)="resetFilters()">Clear filters</button>
            </div>
          } @else {
            <ul class="quote-list">
              @for (quote of filteredQuotes(); track quote.id) {
                <li>
                  <a class="quote-row" [routerLink]="['/quotes', quote.id]">
                    <div class="card-body">
                      <div class="quote-header">
                        <span class="author-avatar"
                              [style.background-color]="avatarColor(quote.author)"
                              aria-hidden="true">{{ initials(quote.author) }}</span>
                        <div class="author-meta">
                          <span class="row-author">{{ quote.author }}</span>
                          @if (quote.createdAt) {
                            <span class="row-date">{{ formatDate(quote.createdAt) }}</span>
                          }
                        </div>
                      </div>
                      <span class="row-text">{{ quote.text }}</span>
                    </div>
                    <span class="card-arrow" aria-hidden="true">
                      <span class="arrow-label">View Details</span>
                      <span class="arrow-icon">→</span>
                    </span>
                  </a>
                </li>
              }
            </ul>

            <!-- Footer summary — synchronized with pagination state -->
            <div class="list-footer" aria-live="polite">
              <span class="footer-info">
                @if (searchTerm() || selectedAuthor()) {
                  {{ filteredQuotes().length }} quote{{ filteredQuotes().length === 1 ? '' : 's' }} match
                } @else if (quotes.estimatedTotal() !== null) {
                  Showing {{ quotes.firstItem() }}–{{ quotes.lastItem() }} of ~{{ quotes.estimatedTotal() }} quotes
                } @else {
                  Showing {{ quotes.firstItem() }}–{{ quotes.lastItem() }}
                }
                @if (quotes.totalPages() !== null) {
                  &nbsp;·&nbsp; Page {{ quotes.page() }} of {{ quotes.totalPages() }}
                }
              </span>
            </div>
          }
        }
      }
    </section>
  `,
  styles: [`
    /* ── Page header: h1 + primary action ───────────────────────── */
    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      margin-bottom: 0.25rem;
    }
    .page-header h1 { margin: 0; }

    .btn-new-quote {
      display: inline-flex;
      align-items: center;
      flex-shrink: 0;
      font: inherit;
      font-size: 0.875rem;
      font-weight: 600;
      line-height: 1.4;
      padding: 0.4rem 1rem;
      border: 0;
      border-radius: 6px;
      background: var(--primary);
      color: #fff;
      text-decoration: none;
      cursor: pointer;
      white-space: nowrap;
      user-select: none;
      transition: background 0.15s, box-shadow 0.15s, transform 0.1s;
    }
    .btn-new-quote:hover {
      background: var(--primary-dark);
      transform: translateY(-1px);
    }
    .btn-new-quote:active { transform: translateY(0); }
    .btn-new-quote:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }

    .btn-new-quote--locked {
      background: var(--btn-locked-bg);
      color: var(--btn-locked-text);
      cursor: not-allowed;
    }
    .btn-new-quote--locked:hover { background: var(--btn-locked-bg); transform: none; }

    /* ── Page container ─────────────────────────────────────────── */
    .list-pane { width: 100%; }

    /* ── Stats dashboard ────────────────────────────────────────── */
    .stats-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 0.75rem;
      margin-bottom: 1.25rem;
    }
    .stat-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 1rem;
      box-shadow: var(--shadow-sm);
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 0.15rem;
      cursor: default;
      transition: transform 0.18s, box-shadow 0.18s;
    }
    .stat-card:hover {
      transform: translateY(-3px);
      box-shadow: var(--shadow-hover);
    }
    .stat-icon { font-size: 1.4rem; line-height: 1; margin-bottom: 0.25rem; }
    .stat-value {
      font-size: 1.5rem;
      font-weight: 700;
      color: var(--primary-text);
      line-height: 1.2;
    }
    .stat-of { font-size: 0.9rem; font-weight: 400; color: var(--text-muted); }
    .stat-label {
      font-size: 0.7rem;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      font-weight: 500;
    }

    /* ── Controls bar ───────────────────────────────────────────── */
    .controls {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      flex-wrap: wrap;
      margin-bottom: 1.25rem;
    }

    /* ── Windowed page buttons ──────────────────────────────────── */
    .page-nav { display: flex; align-items: center; gap: 0.2rem; flex-wrap: wrap; }
    .page-btn {
      font: inherit;
      font-size: 0.82rem;
      min-width: 2.1rem;
      height: 2.1rem;
      padding: 0 0.35rem;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface);
      color: var(--primary);
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s, transform 0.1s;
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }
    .page-btn:hover:not(:disabled):not(.page-btn--active) {
      background: var(--primary-tint);
      border-color: var(--border-hover);
      transform: translateY(-1px);
    }
    .page-btn:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .page-btn:disabled { color: var(--text-disabled); cursor: not-allowed; background: var(--surface-subtle); }
    .page-btn--active {
      background: var(--primary);
      color: #fff;
      border-color: var(--primary);
      font-weight: 600;
      cursor: default;
    }
    .page-btn--nav  { font-size: 1rem; }
    .page-ellipsis  { font-size: 0.85rem; color: var(--text-muted); padding: 0 0.1rem; user-select: none; line-height: 2.1rem; }

    /* ── Page size selector ─────────────────────────────────────── */
    .page-size-row  { display: flex; align-items: center; gap: 0.4rem; }
    .size-lbl       { font-size: 0.8rem; color: var(--text-muted); white-space: nowrap; }
    .size-select {
      font: inherit;
      font-size: 0.82rem;
      padding: 0.3rem 0.4rem;
      border: 1px solid var(--border-muted);
      border-radius: 6px;
      background: var(--surface);
      color: var(--text-primary);
      cursor: pointer;
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    .size-select:focus { outline: none; border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }

    /* ── Skeleton loader ────────────────────────────────────────── */
    .skeleton-list  { display: flex; flex-direction: column; gap: 0.5rem; }
    .skeleton-card  {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 0.85rem 1rem;
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
    }
    .skel {
      background: linear-gradient(90deg, var(--surface-subtle) 25%, var(--border) 50%, var(--surface-subtle) 75%);
      background-size: 200% 100%;
      animation: shimmer 1.4s infinite;
      border-radius: 4px;
    }
    @keyframes shimmer {
      0%   { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }
    .skel-avatar { width: 1.75rem; height: 1.75rem; border-radius: 50%; flex-shrink: 0; }
    .skel-body   { flex: 1; display: flex; flex-direction: column; gap: 0.4rem; padding-top: 0.1rem; }
    .skel-author { height: 0.6rem; width: 35%; }
    .skel-text   { height: 0.75rem; width: 80%; }

    /* ── Sticky filter bar ──────────────────────────────────────── */
    .sticky-filters-wrap {
      position: sticky;
      top: 0;
      z-index: 10;
      background: var(--page-bg);
      padding: 0.25rem 0 0.5rem;
      margin-bottom: 0.25rem;
    }
    .filters {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      flex-wrap: wrap;
      padding: 0.65rem 0.875rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
    }

    /* Search wrapper — icon + input aligned */
    .search-wrapper {
      position: relative;
      flex: 3 1 20rem;
      display: flex;
      align-items: center;
    }
    .search-icon {
      position: absolute;
      left: 0.6rem;
      font-size: 0.82rem;
      pointer-events: none;
      top: 50%;
      transform: translateY(-50%);
      line-height: 1;
    }
    .search-wrapper input {
      font: inherit;
      font-size: 0.85rem;
      padding: 0.38rem 0.6rem 0.38rem 2rem;
      border: 1px solid var(--border-muted);
      border-radius: 6px;
      background: var(--surface);
      color: var(--text-primary);
      width: 100%;
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    .search-wrapper input::placeholder { color: var(--text-muted); font-style: italic; }
    .search-wrapper input:focus {
      outline: none;
      border-color: var(--primary);
      box-shadow: 0 0 0 3px var(--primary-ring);
    }

    /* Author + sort selects */
    .filter-select {
      font: inherit;
      font-size: 0.85rem;
      padding: 0.38rem 0.6rem;
      border: 1px solid var(--border-muted);
      border-radius: 6px;
      background: var(--surface);
      color: var(--text-primary);
      flex: 0 1 auto;
      cursor: pointer;
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    .filter-select:focus {
      outline: none;
      border-color: var(--primary);
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .sort-select { min-width: 7.5rem; }

    .filter-reset {
      font: inherit;
      font-size: 0.8rem;
      padding: 0.38rem 0.8rem;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface-subtle);
      color: var(--text-secondary);
      cursor: pointer;
      flex-shrink: 0;
      transition: background 0.15s, transform 0.1s;
    }
    .filter-reset:hover { background: var(--border); transform: translateY(-1px); }
    .filter-reset:focus-visible { outline: none; box-shadow: 0 0 0 3px var(--primary-ring); }

    /* ── Range / stats line ─────────────────────────────────────── */
    .range-line { font-size: 0.78rem; color: var(--text-muted); margin: 0 0 0.75rem; }

    /* ── Quote list ─────────────────────────────────────────────── */
    .quote-list { list-style: none; display: flex; flex-direction: column; gap: 0.5rem; padding: 0; }

    /* ── Quote card ─────────────────────────────────────────────── */
    .quote-row {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 0.9rem 1rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-left: 3px solid var(--primary);
      border-radius: 8px;
      cursor: pointer;
      text-decoration: none;
      color: inherit;
      /* GPU-composited properties only — no layout or paint triggers */
      will-change: transform, box-shadow;
      transition: background-color 0.2s ease-out,
                  border-color     0.2s ease-out,
                  box-shadow       0.2s ease-out,
                  transform        0.2s ease-out;
    }
    .quote-row:hover {
      background: var(--surface-hover);
      border-color: var(--border-hover);
      border-left-color: var(--primary);
      /* two-layer shadow: neutral base depth + primary-tinted halo */
      box-shadow: var(--shadow-sm), var(--shadow-hover);
      transform: translateY(-3px);
    }
    .quote-row:active {
      transform: translateY(0);
      box-shadow: var(--shadow-sm);
    }
    .quote-row:focus-visible {
      outline: none;
      background: var(--surface-hover);
      border-color: var(--primary);
      /* ring + base depth so keyboard state matches hover depth */
      box-shadow: 0 0 0 3px var(--primary-ring), var(--shadow-sm);
    }

    /* Card content area — takes all remaining space */
    .card-body {
      flex: 1;
      min-width: 0; /* lets text-overflow: ellipsis work inside a flex child */
    }

    /* "View Details →" affordance */
    .card-arrow {
      display: flex;
      align-items: center;
      gap: 0.3rem;
      flex-shrink: 0;
      font-size: 0.78rem;
      font-weight: 500;
      color: var(--text-disabled);
      white-space: nowrap;
      transition: color 0.2s ease-out;
    }
    .arrow-icon {
      display: inline-block;
      font-size: 0.85rem;
      transition: transform 0.2s ease-out;
    }
    .quote-row:hover .card-arrow  { color: var(--primary); }
    .quote-row:hover .arrow-icon  { transform: translateX(4px); }

    /* Card header: avatar + author + date */
    .quote-header { display: flex; align-items: center; gap: 0.55rem; margin-bottom: 0.5rem; }
    .author-avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.85rem;
      height: 1.85rem;
      border-radius: 50%;
      font-size: 0.58rem;
      font-weight: 700;
      color: #fff;
      flex-shrink: 0;
      letter-spacing: 0.02em;
      text-transform: uppercase;
      transition: transform 0.2s ease-out;
    }
    .quote-row:hover .author-avatar { transform: scale(1.1); }

    .author-meta { display: flex; flex-direction: column; gap: 0.05rem; }
    .row-author {
      font-weight: 600;
      font-size: 0.82rem;
      color: var(--primary-text);
      letter-spacing: 0.01em;
    }
    .row-date { font-size: 0.68rem; color: var(--text-disabled); }

    /* Quote text */
    .row-text {
      display: block;
      font-size: 0.875rem;
      color: var(--text-secondary);
      line-height: 1.55;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    /* ── Empty state ────────────────────────────────────────────── */
    .empty-state {
      display: flex; flex-direction: column;
      align-items: center; text-align: center;
      padding: 3rem 1rem; gap: 0.5rem;
    }
    .empty-icon  { font-size: 2.5rem; line-height: 1; margin-bottom: 0.25rem; }
    .empty-title { font-size: 1rem; font-weight: 600; color: var(--text-primary); }
    .empty-sub   { font-size: 0.85rem; color: var(--text-muted); }
    .empty-reset {
      font: inherit; font-size: 0.85rem;
      padding: 0.45rem 1.1rem; margin-top: 0.5rem;
      border: 1px solid var(--primary); border-radius: 6px;
      background: transparent; color: var(--primary);
      cursor: pointer; transition: background 0.15s;
    }
    .empty-reset:hover { background: var(--primary-tint); }
    .empty-reset:focus-visible { outline: none; box-shadow: 0 0 0 3px var(--primary-ring); }

    /* ── Status messages ────────────────────────────────────────── */
    .status-msg       { color: var(--text-muted); padding: 1rem 0; font-size: 0.9rem; }
    .status-msg--error { color: var(--danger); }

    /* ── Footer summary ─────────────────────────────────────────── */
    .list-footer {
      margin-top: 1.25rem;
      padding: 0.7rem 1rem;
      background: var(--surface-subtle);
      border: 1px solid var(--border);
      border-radius: 8px;
      text-align: center;
    }
    .footer-info { font-size: 0.8rem; color: var(--text-muted); }

    /* ── Responsive ─────────────────────────────────────────────── */
    @media (max-width: 600px) {
      .stats-grid  { gap: 0.5rem; }
      .stat-value  { font-size: 1.25rem; }
      .sort-select { min-width: unset; }
    }
    @media (max-width: 420px) {
      .stats-grid   { grid-template-columns: 1fr 1fr; }
      .filters      { padding: 0.5rem 0.75rem; }
      .quote-row    { padding: 0.75rem 0.875rem; gap: 0.6rem; }
      .quote-row:hover { transform: none; }
      .page-btn     { min-width: 1.85rem; height: 1.85rem; font-size: 0.78rem; }
      .arrow-label  { display: none; }
    }
  `],
})
export class QuotesListComponent {

  protected readonly quotes = inject(QuotesService);
  protected readonly auth   = inject(AuthService);

  // ── Filter + sort signals ─────────────────────────────────────────
  protected readonly searchTerm     = signal<string>('');
  protected readonly selectedAuthor = signal<string>('');
  protected readonly sortBy         = signal<SortOption>('default');

  protected readonly filteredQuotes = computed<Quote[]>(() => {
    const term   = this.searchTerm().toLowerCase().trim();
    const author = this.selectedAuthor();
    const sort   = this.sortBy();

    let result = this.quotes.quotes().filter(q => {
      const matchesText   = !term   || q.text.toLowerCase().includes(term)
                                    || q.author.toLowerCase().includes(term);
      const matchesAuthor = !author || q.author === author;
      return matchesText && matchesAuthor;
    });

    switch (sort) {
      case 'author-asc':
        result = [...result].sort((a, b) => a.author.localeCompare(b.author));
        break;
      case 'author-desc':
        result = [...result].sort((a, b) => b.author.localeCompare(a.author));
        break;
      case 'newest':
        result = [...result].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
        break;
      case 'oldest':
        result = [...result].sort((a, b) => a.createdAt.localeCompare(b.createdAt));
        break;
      default:
        break;
    }

    return result;
  });

  protected readonly totalCount   = computed<number>(() => this.quotes.quotes().length);
  protected readonly authors       = computed<string[]>(() =>
    [...new Set(this.quotes.quotes().map(q => q.author))].sort());
  protected readonly statsAuthors  = computed<number>(() =>
    new Set(this.quotes.quotes().map(q => q.author)).size);
  protected readonly viewStatus    = computed<string>(() => this.quotes.listView().status);

  resetFilters(): void {
    this.searchTerm.set('');
    this.selectedAuthor.set('');
    this.sortBy.set('default');
  }

  protected readonly errorMessage = computed<string>(() => {
    const view = this.quotes.listView();
    return view.status === 'error' ? view.message : '';
  });

  // ── Windowed pagination ───────────────────────────────────────────
  protected readonly pageWindow = computed<Array<number | string>>(() => {
    const pages = this.quotes.availablePages();
    const total = pages.length;
    const cur   = this.quotes.page();
    if (total <= 7) return pages;
    const result: Array<number | string> = [1];
    if (cur > 3)         result.push('left-gap');
    const lo = Math.max(2, cur - 1);
    const hi = Math.min(total - 1, cur + 1);
    for (let i = lo; i <= hi; i++) result.push(i);
    if (cur < total - 2) result.push('right-gap');
    if (total > 1)       result.push(total);
    return result;
  });

  protected isGap(p: number | string): p is string { return typeof p === 'string'; }
  protected asNum(p: number | string): number       { return p as number; }

  // ── Avatar helpers ────────────────────────────────────────────────
  private readonly _palette = [
    '#0d6efd', '#198754', '#6f42c1', '#d63384',
    '#0dcaf0', '#fd7e14', '#20c997', '#6610f2',
  ];

  protected initials(author: string): string {
    return author.trim().split(/\s+/).slice(0, 2)
      .map(w => w[0]?.toUpperCase() ?? '').join('');
  }

  protected avatarColor(author: string): string {
    let h = 0;
    for (let i = 0; i < author.length; i++) {
      h = (Math.imul(31, h) + author.charCodeAt(i)) | 0;
    }
    return this._palette[Math.abs(h) % this._palette.length];
  }

  // ── Date formatter ────────────────────────────────────────────────
  protected formatDate(iso: string): string {
    if (!iso) return '';
    try {
      return new Date(iso).toLocaleDateString(undefined, {
        month: 'short', day: 'numeric', year: 'numeric',
      });
    } catch {
      return '';
    }
  }

  protected readonly skeletonItems = [1, 2, 3, 4, 5];
}
