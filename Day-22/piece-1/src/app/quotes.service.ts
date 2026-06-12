import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { firstValueFrom }                  from 'rxjs';
import { Quote }                           from './models/quote';
import { CreateQuoteRequest }              from './models/quote';
import { AppError, toAppError }            from './models/app-error';
import { ViewState }                       from './models/view-state';

// ── QuotesService — Day-16 piece-2 SIGNALS-FIRST STORE ─────────────────────
//
// piece-1 exposed raw resource signals (listLoading / listError / quotes) and
// left the component to reassemble the state machine with nested @if/@else.
// piece-2 makes this a proper signals-first STORE: the feature's state is
// MODELLED here as derived computed selectors, and the component just reads
// them. No NgRx, no @ngrx/signals — plain signal() + computed() is enough at
// this scale (the threshold where it stops being enough is argued in README).
//
// Backed by the real Week-1 API:
//   list   → GET /api/quotes?page={page}&size={size}   (Quote[], !IsDeleted, paged)
//   detail → GET /api/quotes/{id}                       (Quote or 404)
//   create → POST /api/quotes                           (CreateQuoteRequest → Quote)
//
// httpResource() (not HttpClient + subscribe) because Day-13 is signals-first:
// .value() / .isLoading() / .error() are signals, and the resource RE-FETCHES
// when a signal its URL factory reads changes. When that signal changes mid-
// flight it ABORTS the previous request — which is what prevents the stale-
// response race when the user clicks quote A then quote B rapidly.
@Injectable({ providedIn: 'root' })
export class QuotesService {

  private readonly http = inject(HttpClient);

  // ── List state ────────────────────────────────────────────────────
  // Page is 1-based to match the server: GetAllAsync uses (page-1)*size.
  readonly page = signal<number>(1);
  readonly size = signal<number>(10);

  // GET /api/quotes?page={page}&size={size}. Re-fires on page()/size() change.
  readonly listResource = httpResource<Quote[]>(
    () => `/api/quotes?page=${this.page()}&size=${this.size()}`,
  );

  // ── Optimistic-create overlay (concurrent-update support) ──────────
  // When the user creates a quote we prepend it to this overlay IMMEDIATELY
  // so the new row appears before the server confirms. On the POST's success
  // we clear the overlay and reload from the server (authoritative); on
  // failure we clear it too (rollback). Holding it separately from the
  // server list means an in-flight list refetch can never clobber the
  // optimistic row, and a failed create can never leave a phantom row behind.
  private readonly optimisticQuotes = signal<Quote[]>([]);

  // ── Derived selectors the components read ─────────────────────────
  // The authoritative list = server rows with any still-pending optimistic
  // rows prepended. computed() recomputes whenever either signal changes.
  readonly quotes = computed<Quote[]>(() => [
    ...this.optimisticQuotes(),
    ...(this.listResource.value() ?? []),
  ]);

  // THE STATE MODEL: one computed discriminated union instead of three loose
  // signals. The template @switch-es on listView().status — it cannot render
  // an impossible combination and cannot forget the empty branch.
  readonly listView = computed<ViewState<Quote[]>>(() => {
    if (this.listResource.isLoading()) return { status: 'loading' };

    const err = this.listResource.error();
    if (err) {
      return {
        status: 'error',
        message: err instanceof AppError
          ? err.message
          : 'Failed to load quotes. Is the Week-1 API running on :5075?',
      };
    }

    const data = this.quotes();
    if (data.length === 0) return { status: 'empty' };
    return { status: 'loaded', data };
  });

  // Kept for components/tests that still read the raw signals.
  readonly listLoading = this.listResource.isLoading;
  readonly listError   = this.listResource.error;

  // ── Detail state ──────────────────────────────────────────────────
  // null = nothing selected → no detail request is issued.
  readonly selectedId = signal<number | null>(null);

  readonly detailResource = httpResource<Quote>(() => {
    const id = this.selectedId();
    return id === null ? undefined : `/api/quotes/${id}`;
  });

  readonly selectedQuote = computed<Quote | null>(() => this.detailResource.value() ?? null);
  readonly detailLoading = this.detailResource.isLoading;
  readonly detailError   = this.detailResource.error;

  // detailView — same ViewState model for the detail pane. `empty` here means
  // "nothing selected yet" (selectedId === null) vs loading/error/loaded.
  readonly detailView = computed<ViewState<Quote>>(() => {
    if (this.selectedId() === null)      return { status: 'empty' };
    if (this.detailResource.isLoading()) return { status: 'loading' };

    const err = this.detailResource.error();
    if (err) {
      return {
        status: 'error',
        message: err instanceof AppError ? err.message : 'Could not load this quote.',
      };
    }

    const q = this.detailResource.value();
    return q ? { status: 'loaded', data: q } : { status: 'loading' };
  });

  // ── Create (write path) ───────────────────────────────────────────
  // POST /api/quotes is a COMMAND, not a reactive read.
  readonly submitting  = signal<boolean>(false);
  readonly submitError = signal<string | null>(null);
  readonly lastCreated = signal<Quote | null>(null);

  // Optimistic create: the new quote appears in the list IMMEDIATELY, then is
  // reconciled against the server's authoritative response (success) or rolled
  // back (failure). The temporary row uses a negative id so it can never
  // collide with a real server id and `track quote.id` stays stable.
  async createQuote(input: CreateQuoteRequest): Promise<Quote> {
    this.submitting.set(true);
    this.submitError.set(null);

    // Optimistic row — negative id guarantees no collision with server ids.
    const tempId = -Date.now();
    const optimistic: Quote = {
      id:        tempId,
      author:    input.author,
      text:      input.text,
      createdAt: new Date().toISOString(),
      isDeleted: false,
      ownerId:   null,
    };
    this.optimisticQuotes.update(list => [optimistic, ...list]);

    try {
      const created = await firstValueFrom(this.http.post<Quote>('/api/quotes', input));
      this.lastCreated.set(created);
      // Reconcile: drop the temp row, then reload to pull the authoritative
      // server row (with its real id/createdAt/ownerId).
      this.optimisticQuotes.update(list => list.filter(q => q.id !== tempId));
      this.listResource.reload();
      return created;
    } catch (err: unknown) {
      // Rollback: remove the optimistic row so a failed create leaves no trace.
      this.optimisticQuotes.update(list => list.filter(q => q.id !== tempId));
      this.submitError.set(this.describeError(err));
      throw err;
    } finally {
      this.submitting.set(false);
    }
  }

  // errorInterceptor maps every API failure to a typed AppError whose .message
  // is already friendly. The HttpErrorResponse branch is an unmapped fallback.
  describeError(err: unknown): string {
    if (err instanceof AppError)          return err.message;
    if (err instanceof HttpErrorResponse) return toAppError(err).message;
    return 'Unexpected error adding the quote.';
  }

  // ── Pagination metadata (derived from raw API response) ──────────
  // The API returns Quote[] with no total-count envelope, so we infer
  // last-page status: if the server returned fewer rows than the page
  // size, there are no more pages. This is exact for all pages except
  // the final full page when total % size === 0 (one extra click, then
  // the empty branch renders and disables Next).
  private readonly rawCount = computed<number>(
    () => this.listResource.value()?.length ?? 0,
  );
  readonly isLastPage = computed<boolean>(() => this.rawCount() < this.size());
  readonly firstItem  = computed<number>(
    () => (this.page() - 1) * this.size() + 1,
  );
  readonly lastItem   = computed<number>(
    () => (this.page() - 1) * this.size() + this.rawCount(),
  );

  // ── Page-inventory watermark ───────────────────────────────────────
  // Because the API returns no total count, we build the known page list
  // incrementally as the user navigates.
  //
  // _hwm  — highest page number confirmed to have data.
  // _confirmedTotal — set once we land on a partial/empty page (rawCount <
  //   size), giving us the exact last page.  Cleared when setSize() is
  //   called because a different size produces a completely different page
  //   structure.
  private readonly _hwm             = signal<number>(1);
  private readonly _confirmedTotal  = signal<number | null>(null);
  private readonly _lastPageCount   = signal<number | null>(null);

  // Update the watermark after each settled list fetch.
  // Runs in the service constructor so no component lifecycle is needed.
  private readonly _inventoryEffect = effect(() => {
    const status = this.listView().status;
    const page   = this.page();
    const isLast = this.isLastPage();

    // Only act when the fetch has settled (not while loading or errored).
    if (status !== 'loaded' && status !== 'empty') return;

    const definitelyLast = isLast || status === 'empty';

    if (definitelyLast) {
      // Landed on a partial or empty page → we know exactly how many pages exist.
      this._confirmedTotal.set(page);
      this._hwm.set(page);
      this._lastPageCount.set(this.rawCount());
    } else {
      // Full page of results → update high-water mark upward only.
      if (page >= this._hwm()) {
        this._hwm.set(page);
        // If we previously confirmed a lower total, that was stale; clear it.
        const ct = this._confirmedTotal();
        if (ct !== null && page >= ct) this._confirmedTotal.set(null);
      }
    }
  });

  private resetPageInventory(): void {
    this._hwm.set(1);
    this._confirmedTotal.set(null);
    this._lastPageCount.set(null);
  }

  // Page numbers available in the direct-navigation dropdown.
  // If the total is confirmed, list exactly those pages.
  // Otherwise list 1…hwm+1 (one speculative page ahead to allow forward nav).
  readonly availablePages = computed<number[]>(() => {
    const total   = this._confirmedTotal();
    const hwm     = this._hwm();
    const current = this.page();
    const max     = total ?? Math.max(hwm + 1, current);
    return Array.from({ length: Math.max(max, 1) }, (_, i) => i + 1);
  });

  // Non-null only once the last page has been confirmed by observation.
  readonly totalPages = computed<number | null>(() => this._confirmedTotal());

  // Approximate total item count — only non-null after reaching the last page.
  // Computed as (confirmedTotal - 1) * size + lastPageItemCount.
  readonly estimatedTotal = computed<number | null>(() => {
    const total     = this._confirmedTotal();
    const lastCount = this._lastPageCount();
    if (total === null || lastCount === null) return null;
    return (total - 1) * this.size() + lastCount;
  });

  // ── Commands ──────────────────────────────────────────────────────
  selectQuote(id: number): void { this.selectedId.set(id); }
  clearSelection(): void        { this.selectedId.set(null); }
  nextPage(): void              { this.page.update(p => p + 1); }
  prevPage(): void              { this.page.update(p => Math.max(1, p - 1)); }
  goToPage(n: number): void     { this.page.set(n); }

  // Change page size and reset to page 1 so existing data is not stale.
  // Also wipes the page inventory because a different size gives a
  // completely different page structure.
  setSize(n: number): void {
    this.size.set(n);
    this.page.set(1);
    this.resetPageInventory();
  }
}
