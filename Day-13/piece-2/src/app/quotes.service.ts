import { Injectable, computed, signal } from '@angular/core';
import { httpResource }                  from '@angular/common/http';
import { Quote }                         from './models/quote';

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
