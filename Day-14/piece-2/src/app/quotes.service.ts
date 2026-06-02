import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { firstValueFrom }                  from 'rxjs';
import { Quote }                           from './models/quote';
import { CreateQuoteRequest }              from './models/quote';

// Signals-first service for the quotes LIST + DETAIL screen, backed by the
// real Week-1 API:
//
//   list   → GET /api/quotes?page={page}&size={size}   (Quote[], !IsDeleted, paged)
//   detail → GET /api/quotes/{id}                       (Quote or 404)
//
// Both endpoints are exposed as separate httpResource()s so the component
// gets independent loading / error / data signals for each pane — the list
// can be loaded while a detail is still fetching, and vice-versa.
//
// httpResource() is used (not HttpClient + subscribe) because Day-13 is
// signals-first: .value() / .isLoading() / .error() are all signals, and the
// resource RE-FETCHES automatically whenever a signal its URL factory reads
// changes. Critically, when that signal changes mid-flight, httpResource
// ABORTS the previous request — which is exactly what prevents the stale-
// response race when the user clicks quote A then quote B rapidly. The
// aborted A response can never overwrite B's detail.
//
// inject() pattern: components consume this with
//   private readonly quotes = inject(QuotesService);
@Injectable({ providedIn: 'root' })
export class QuotesService {

  // ── List state ────────────────────────────────────────────────────
  // Page is 1-based to match the server: GetAllAsync uses (page-1)*size.
  readonly page = signal<number>(1);
  readonly size = signal<number>(10);

  // GET /api/quotes?page={page}&size={size} → proxied to :5075.
  // Re-fires whenever page() or size() changes.
  readonly listResource = httpResource<Quote[]>(
    () => `/api/quotes?page=${this.page()}&size=${this.size()}`,
  );

  // List projections the component reads directly.
  readonly quotes      = computed<Quote[]>(() => this.listResource.value() ?? []);
  readonly listLoading = this.listResource.isLoading;
  readonly listError   = this.listResource.error;

  // ── Detail state ──────────────────────────────────────────────────
  // null = nothing selected → no detail request is issued.
  readonly selectedId = signal<number | null>(null);

  // GET /api/quotes/{id}. The URL factory returns undefined when nothing is
  // selected, which tells httpResource to stay idle (no request fired).
  // Selecting a different id aborts any in-flight detail request — the
  // stale-response race is handled by the resource itself, not by us.
  readonly detailResource = httpResource<Quote>(() => {
    const id = this.selectedId();
    return id === null ? undefined : `/api/quotes/${id}`;
  });

  readonly selectedQuote = computed<Quote | null>(() => this.detailResource.value() ?? null);
  readonly detailLoading = this.detailResource.isLoading;
  readonly detailError   = this.detailResource.error;

  // ── Create (write path) ───────────────────────────────────────────
  // POST /api/quotes is a COMMAND, not a reactive read, so it uses HttpClient
  // directly rather than httpResource. The form drives these signals:
  //   submitting   → disables the button / sets aria-busy while in flight
  //   submitError  → human message shown in the form's server-error alert
  //   lastCreated  → the 201 body, used to announce success in a live region
  private readonly http = inject(HttpClient);

  readonly submitting  = signal<boolean>(false);
  readonly submitError = signal<string | null>(null);
  readonly lastCreated = signal<Quote | null>(null);

  // Returns the created Quote (201 body) or throws. On success the list is
  // reloaded so the new quote appears without a manual refresh.
  async createQuote(input: CreateQuoteRequest): Promise<Quote> {
    this.submitting.set(true);
    this.submitError.set(null);
    try {
      const created = await firstValueFrom(
        this.http.post<Quote>('/api/quotes', input),
      );
      this.lastCreated.set(created);
      this.listResource.reload(); // refresh the list to include the new quote
      return created;
    } catch (err: unknown) {
      this.submitError.set(this.describeError(err));
      throw err;
    } finally {
      this.submitting.set(false);
    }
  }

  // Bare POST /api/quotes — no signal side effects. The Signal Forms version
  // (quote-form-signals.component) drives submit/error state via the form's own
  // submit() + submitting() signals, so it uses this instead of createQuote().
  postQuote(input: CreateQuoteRequest): Promise<Quote> {
    return firstValueFrom(this.http.post<Quote>('/api/quotes', input))
      .then(created => { this.listResource.reload(); return created; });
  }

  // Maps the real failure modes of POST /api/quotes to user-facing copy.
  // No `any`: err is unknown and narrowed to HttpErrorResponse.
  // Public so both the reactive and Signal Forms versions reuse one mapping.
  describeError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      switch (err.status) {
        case 0:        return 'Could not reach the API. Is it running on :5075?';
        case 400:      return 'The server rejected the quote (check the author/text limits).';
        case 401:
        case 403:      return 'You must be signed in with edit rights to add a quote.';
        default:       return `Unexpected error adding the quote (HTTP ${err.status}).`;
      }
    }
    return 'Unexpected error adding the quote.';
  }

  // ── Commands ──────────────────────────────────────────────────────
  selectQuote(id: number): void {
    this.selectedId.set(id);
  }

  clearSelection(): void {
    this.selectedId.set(null);
  }

  nextPage(): void {
    this.page.update(p => p + 1);
  }

  prevPage(): void {
    this.page.update(p => Math.max(1, p - 1));
  }
}
