# Day 13 · Piece 2 — A Real Component from a Spec (list + detail)

Quotes **list + detail** standalone Angular 21 component against the real Week-1 API. Signals for loading/error/data on both panes, `inject()` for the service, fully typed model (no `any`), new control flow, and a handled stale-response race.

---

## (1) The brief I gave the agent

> Build a quotes **list + detail** component in `Day-13/piece-2` against my real Week-1 API. Two endpoints, exact contract:
>
> - **List:** `GET /api/quotes?page={page}&size={size}` → returns `Quote[]`, already filtered to `!IsDeleted`, paged. **Paging is 1-based** (server does `(page-1)*size`).
> - **Detail:** `GET /api/quotes/{id}` → returns a single `Quote` (200) or `404`.
> - **`Quote` fields (serialised EF entity):** `id: number`, `author: string`, `text: string`, `createdAt: string`, `isDeleted: boolean`, `ownerId: number | null`. The text field is **`text`**, not `body`/`content`.
>
> Requirements:
> - Standalone (no NgModule). Zoneless is already configured.
> - **Signals for loading / error / data — independently for the list and the detail** (the list must stay rendered while a detail loads).
> - `inject()` for the service, no constructor injection.
> - Model fully typed — **no `any` anywhere**, including the error branches.
> - New control flow: `@if`/`@else if`/`@for (track)`/`@switch`.
> - **Handle the stale-response race:** if I click quote A then quote B before A's detail returns, B's detail must win — A's late response must never overwrite it.
> - Use `httpResource` (signals-first), proxied via the existing `proxy.conf.js` to `:5075`.

---

## (2) The agent's output

### `src/app/quotes.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class QuotesService {
  // List — 1-based paging to match the server.
  readonly page = signal<number>(1);
  readonly size = signal<number>(10);

  readonly listResource = httpResource<Quote[]>(
    () => `/api/quotes?page=${this.page()}&size=${this.size()}`,
  );
  readonly quotes      = computed<Quote[]>(() => this.listResource.value() ?? []);
  readonly listLoading = this.listResource.isLoading;
  readonly listError   = this.listResource.error;

  // Detail — null = nothing selected → no request fired.
  readonly selectedId = signal<number | null>(null);

  readonly detailResource = httpResource<Quote>(() => {
    const id = this.selectedId();
    return id === null ? undefined : `/api/quotes/${id}`;
  });
  readonly selectedQuote = computed<Quote | null>(() => this.detailResource.value() ?? null);
  readonly detailLoading = this.detailResource.isLoading;
  readonly detailError   = this.detailResource.error;

  selectQuote(id: number): void { this.selectedId.set(id); }
  clearSelection(): void        { this.selectedId.set(null); }
  nextPage(): void              { this.page.update(p => p + 1); }
  prevPage(): void              { this.page.update(p => Math.max(1, p - 1)); }
}
```

The stale-race protection is structural: `detailResource`'s URL factory reads `selectedId()`. When it changes mid-flight, `httpResource` **aborts** the previous request, so a late A response can't land after B.

### `src/app/quotes-list/quotes-list.component.ts` (key parts)

```typescript
@Component({
  selector: 'app-quotes-list',
  standalone: true,
  template: `
    <!-- LIST pane: independent loading/error/empty/data state machine -->
    @if (quotes.listLoading()) {
      <p class="status">Loading quotes…</p>
    } @else if (quotes.listError()) {
      <p class="status error">Failed to load quotes. Is the Week-1 API running on :5075?</p>
    } @else if (quotes.quotes().length === 0) {
      <p class="status empty">No quotes on this page.</p>
    } @else {
      <ul class="quote-list">
        @for (quote of quotes.quotes(); track quote.id) {
          <li class="quote-row"
              [class.selected]="quote.id === quotes.selectedId()"
              (click)="quotes.selectQuote(quote.id)">
            <span class="row-author">{{ quote.author }}</span>
            <span class="row-text">{{ quote.text }}</span>
          </li>
        }
      </ul>
    }

    <!-- DETAIL pane: separate @switch state machine -->
    @switch (detailState()) {
      @case ('idle')    { <p class="status">Select a quote to see its detail.</p> }
      @case ('loading') { <p class="status">Loading quote {{ quotes.selectedId() }}…</p> }
      @case ('error')   { <p class="status error">Couldn't load quote {{ quotes.selectedId() }} (404?).</p> }
      @case ('loaded')  {
        <article class="detail-card">
          <p class="detail-text">"{{ quotes.selectedQuote()!.text }}"</p>
          <p class="detail-author">— {{ quotes.selectedQuote()!.author }}</p>
          <dl class="detail-meta">
            <dt>id</dt>        <dd>{{ quotes.selectedQuote()!.id }}</dd>
            <dt>createdAt</dt> <dd>{{ quotes.selectedQuote()!.createdAt }}</dd>
            <dt>ownerId</dt>   <dd>{{ quotes.selectedQuote()!.ownerId ?? '—' }}</dd>
          </dl>
        </article>
      }
    }
  `,
})
export class QuotesListComponent {
  protected readonly quotes = inject(QuotesService);   // inject(), no constructor

  protected detailState(): 'idle' | 'loading' | 'error' | 'loaded' {
    if (this.quotes.selectedId() === null) return 'idle';
    if (this.quotes.detailLoading())       return 'loading';
    if (this.quotes.detailError())         return 'error';
    if (this.quotes.selectedQuote())       return 'loaded';
    return 'idle';
  }
}
```

---

## (3) Verification log

Run: API on `:5075` (`cd QuotesApi && dotnet run`), app on `:4200` (`npm start`).

### States / edges exercised

| State / edge | How triggered | Result | ✓ |
|---|---|---|---|
| **List loading** | First page load | "Loading quotes…" while `/api/quotes?page=1&size=10` is in flight | ✅ |
| **List data** | API responds | `@for` renders 10 quote rows, each author + truncated text | ✅ |
| **List empty** | Click Next past the last page (e.g. page 99) | `quotes().length === 0` → "No quotes on this page." (`@else if` empty branch) | ✅ |
| **List error** | Stop the API, reload | Timeout interceptor fires → "Failed to load quotes…" (`@else if listError()`) | ✅ |
| **Detail idle** | Nothing selected | "Select a quote to see its detail." | ✅ |
| **Detail loading** | Click a row | "Loading quote {id}…" while `/api/quotes/{id}` fetches | ✅ |
| **Detail data** | Response arrives | Detail card shows text, author, id, createdAt, ownerId | ✅ |
| **Detail error (404)** | `selectedId.set(999999)` for a non-existent id | `detailError()` truthy → "Couldn't load quote 999999 (404?)" | ✅ |
| **Stale-response race** | Click quote A, then quote B within ~50 ms | B's detail renders and **stays**; A's late response is aborted, never overwrites B | ✅ |
| **List+detail interleave** | Trigger a page change while a detail is loading | Both panes show their own loading state independently; neither clobbers the other | ✅ |

### The stale-race test, concretely

With the detail endpoint artificially slowed (a `Task.Delay` during testing), I clicked quote `id=1` then immediately `id=2`. The Network tab showed `GET /api/quotes/1` flip to **(canceled)** the instant `id=2` was selected; only `GET /api/quotes/2` resolved, and the detail pane showed quote 2. Without abort, the slower-arriving quote-1 response would have overwritten quote 2 — the classic last-write-wins-by-latency bug. `httpResource` aborts on URL-signal change, so it can't happen.

### One concrete thing the agent got wrong — and made it fix

**Wrong field name carried over from piece-1.** The first model the agent produced kept piece-1's `Quote` shape:

```typescript
export interface Quote {
  id: number; author: string; text: string;
  createdAt: string;
  addedAt: string;   // ← WRONG for /api/quotes
}
```

`addedAt` is a **collection-membership** field that only exists on `CollectionItems` (the Day-12 `GET /api/collections/{id}` read model). The `/api/quotes` endpoint serialises the `Quote` **entity**, which has no `addedAt` — it has `isDeleted` and `ownerId` instead. Because TS interfaces are erased at runtime, `httpResource<Quote>` would have happily returned objects whose `addedAt` was `undefined`, and any binding to it would have silently rendered blank — a green, wrong result.

I made it fix the model to match the real entity:

```typescript
export interface Quote {
  id: number; author: string; text: string;
  createdAt: string;
  isDeleted: boolean;       // real entity field
  ownerId:   number | null; // int? → number | null
}
```

(Smaller catch: the agent first typed the detail error handler parameter as `(err: any)`. I had it removed — the component reads `detailError()` as the resource's typed error signal instead, keeping the "no `any`" rule.)

### What breaks if the Week-1 API contract changes

- **Field rename (`text` → `body`):** `httpResource<Quote>` still returns HTTP 200; `quote.text` becomes `undefined`; list rows and detail card render **blank** with no error. TS can't catch it (interface erased at runtime) and there's no response schema validation. Highest-risk seam — a zod parse on the response would turn the silent blank into a visible error.
- **Paging contract change (1-based → 0-based):** `prevPage()` clamps at 1; if the server switched to 0-based, page 1 would silently skip the first `size` rows. The off-by-one is invisible — no error, just missing data at the top.
- **Detail returns 200 + `null` instead of 404 for a missing id:** `selectedQuote()` would be `null`, `detailState()` falls through to `'idle'`, and the pane shows "Select a quote…" instead of an error — a misleading state. Current code assumes "missing = 404".
- **`ownerId` becomes a string (GUID auth migration):** `number | null` would mistype it; `?? '—'` still renders, but any numeric use downstream breaks. Caught only at the next place the field is used arithmetically.

---

## How to run

```bash
# Terminal 1 — the API
cd Day-13/piece-2/QuotesApi
dotnet run                      # → http://localhost:5075

# Terminal 2 — the Angular app
cd Day-13/piece-2
npm install
npm start                       # → http://localhost:4200  (/api proxied to :5075)
```

Click a quote row → detail loads in the right pane. Use Prev/Next to page the list. Both panes carry their own loading/error/empty state and never block each other.
