# Day 15 · Piece 1 — HttpClient + functional interceptors (directed by an agent)

Characterization-test-first wiring of `HttpClient` + functional interceptors against the real
Week-1 API: **auth header**, **retry idempotent GETs with exponential backoff**, and **ProblemDetails
→ typed `AppError`** that surfaces a friendly message on a 4xx. The contract is pinned by a green
test **before** any UI work.

Built on Day-14 piece-2. Test runner: Angular 21 `@angular/build:unit-test` (Vitest).

---

## (1) The brief I gave the agent

> In `Day-15/piece-1`, wire `HttpClient` + functional interceptors against my **real** Week-1 API. **Write the characterization test first and get it green before touching any UI.**
>
> **Real contract (read the C#, don't assume):**
> - **List:** `GET /api/quotes?page=N&size=N` → `Quote[]`. Each item is the serialised EF entity (System.Text.Json camelCase): `{ id: number, author: string, text: string, createdAt: string, isDeleted: boolean, ownerId: number | null }` (`QuoteRepository.GetAllAsync`).
> - **4xx shape:** the API returns **`ProblemDetails`**, not `ValidationProblemDetails`. A `400` from a domain rule comes from `ExceptionMiddleware` as `{ "title":"Bad Request", "detail":"<domain message>", "status":400 }` ([ExceptionMiddleware.cs:31-37](QuotesApi/Middleware/ExceptionMiddleware.cs#L31-L37)); `401` from auth is `Results.Problem("Invalid email or password.", 401)`. Verify this before writing the mapper.
>
> **Deliverables:**
> 1. A **characterization test** (`HttpTestingController`) pinning: the `Quote[]` shape; the Bearer header; a `400` ProblemDetails → typed error with the server `detail` as the message; retry-with-backoff on a transient GET; and NO retry on 4xx / on POST.
> 2. Functional interceptors: **auth** (Bearer on `/api`, skip `/api/auth/*`); **retry** idempotent (GET-only) on transient (`0`/`429`/`5xx`) with exponential backoff, never on 4xx or non-GET; **error** mapping ProblemDetails → a typed `AppError` with a friendly `.message`.
> 3. One typed `AppError`; every consumer reads `.message`. No `any`.

---

## (2) The agent's output

### Characterization test — `src/app/http-contract.spec.ts` (green before UI)

```typescript
const LIST_URL = '/api/quotes?page=1&size=10';
const tick = () => new Promise<void>(resolve => setTimeout(resolve, 0)); // flush timer(0) backoff

beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor, authInterceptor, retryInterceptor])),
      provideHttpClientTesting(),
      { provide: RETRY_CONFIG, useValue: { maxRetries: 2, baseDelayMs: 0 } },
    ],
  });
  http = TestBed.inject(HttpClient);
  mock = TestBed.inject(HttpTestingController);
});
afterEach(() => mock.verify());

it('1. GET /api/quotes?page&size returns the documented Quote[] shape', async () => {
  let result: Quote[] | undefined;
  http.get<Quote[]>(LIST_URL).subscribe(r => (result = r));
  const req = mock.expectOne(LIST_URL);
  expect(req.request.method).toBe('GET');
  req.flush([{ id: 1, author: 'Marcus Aurelius', text: 'The impediment to action advances action.',
               createdAt: '2026-01-01T00:00:00Z', isDeleted: false, ownerId: 1 }]);
  await tick();
  const q = result![0];
  expect(typeof q.id).toBe('number');
  expect(typeof q.author).toBe('string');
  expect(typeof q.text).toBe('string');
});

it('3. maps a 400 ProblemDetails to a typed AppError carrying the server detail', async () => {
  let caught: unknown;
  http.post('/api/quotes', { author: '', text: 'x' }).subscribe({ error: e => (caught = e) });
  mock.expectOne('/api/quotes').flush(
    { title: 'Bad Request', detail: 'Author must be between 1 and 200 characters.', status: 400 },
    { status: 400, statusText: 'Bad Request' });
  await tick();
  expect(caught).toBeInstanceOf(AppError);
  expect((caught as AppError).status).toBe(400);
  expect((caught as AppError).message).toBe('Author must be between 1 and 200 characters.');
});

it('4. retries an idempotent GET on a transient 503, then succeeds', async () => {
  let result: Quote[] | undefined, caught: unknown;
  http.get<Quote[]>(LIST_URL).subscribe({ next: r => (result = r), error: e => (caught = e) });
  mock.expectOne(LIST_URL).flush('boom', { status: 503, statusText: 'Service Unavailable' }); await tick();
  mock.expectOne(LIST_URL).flush('boom', { status: 503, statusText: 'Service Unavailable' }); await tick();
  mock.expectOne(LIST_URL).flush([{ id: 9, author: 'Seneca', text: '…', createdAt: '…', isDeleted: false, ownerId: null }]); await tick();
  expect(caught).toBeUndefined();
  expect(result?.[0].author).toBe('Seneca');
});

// 2. Bearer header present;  5. NO retry on a 400 GET (expectNone);  6. NO retry on a 503 POST (expectNone)
```

### Auth interceptor — `interceptors/auth.interceptor.ts` (carried from Day-14, unchanged)

```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();
  if (token && req.url.startsWith('/api') && !req.url.startsWith('/api/auth/')) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req);
};
```

### Retry interceptor — `interceptors/retry.interceptor.ts`

```typescript
export const RETRY_CONFIG = new InjectionToken<RetryConfig>('RETRY_CONFIG', {
  factory: () => ({ maxRetries: 2, baseDelayMs: 300 }),
});

function isTransient(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false;       // e.g. TimeoutError → not retried
  return error.status === 0 || error.status === 429 || error.status >= 500;
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') return next(req);                     // idempotent only
  const { maxRetries, baseDelayMs } = inject(RETRY_CONFIG);
  return next(req).pipe(
    retry({
      count: maxRetries,
      delay: (error, retryCount) => {
        if (!isTransient(error)) throw error;                    // 4xx → surface immediately
        return timer(baseDelayMs * 2 ** (retryCount - 1));       // 300ms, 600ms (exponential)
      },
    }),
  );
};
```

### Typed error mapping — `models/app-error.ts` + `interceptors/error.interceptor.ts`

```typescript
export class AppError extends Error {
  constructor(readonly status: number, override readonly message: string,
              readonly detail?: string, readonly fieldErrors?: Record<string, string[]>) {
    super(message); this.name = 'AppError';
  }
}

export function toAppError(err: HttpErrorResponse): AppError {
  const pd = readProblemDetails(err);                            // {type,title,status,detail,errors?}
  if (pd?.errors && Object.keys(pd.errors).length) {             // ValidationProblemDetails (tolerated)
    const k = Object.keys(pd.errors)[0];
    return new AppError(err.status, pd.errors[k]?.[0] ?? pd.detail ?? fallbackMessage(err.status), pd.detail, pd.errors);
  }
  if (pd?.detail) return new AppError(err.status, pd.detail, pd.detail); // plain ProblemDetails (this API)
  return new AppError(err.status, fallbackMessage(err.status));         // status-based friendly fallback
}

// error.interceptor.ts — outermost; maps the FINAL (post-retry) error
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((err: unknown) =>
    throwError(() => err instanceof HttpErrorResponse ? toAppError(err)
                   : err instanceof AppError          ? err
                   : new AppError(0, 'Could not reach the API. Is it running on :5075?'))));
```

Wiring ([app.config.ts](src/app/app.config.ts)) — order is load-bearing:
`withInterceptors([errorInterceptor, authInterceptor, retryInterceptor, timeoutInterceptor])` — retry sees the **raw** `HttpErrorResponse` (to judge retryability); only the final un-retried error reaches `errorInterceptor` to become an `AppError`.

---

## (3) Verification log — grounded in the real Week-1 API

`npx ng test --watch=false` → **6/6 passing** (Vitest, `@angular/build:unit-test`). The contract is pinned **before** UI.

### States / edges exercised

| # | Edge | Pinned behaviour |
|---|---|---|
| 1 | **Data / shape** | `GET /api/quotes?page=1&size=10` → `Quote[]` of `{id:number, author:string, text:string, createdAt, isDeleted, ownerId}` |
| 2 | **Auth header** | request to `/api/quotes` carries `Authorization: Bearer <token>` from `AuthService.token()` |
| 3 | **4xx → friendly** | `400` `ProblemDetails {detail:"Author must be between 1 and 200 characters."}` → `AppError{status:400, message:<that detail>}` |
| 4 | **Retry / transient** | GET that 503s twice then 200s → retried with 300/600 ms backoff, resolves to data (no error surfaced) |
| 5 | **No retry on 4xx** | GET returning `400` → exactly **one** attempt (`expectNone` after) → `AppError(400)` |
| 6 | **No retry on POST** | `POST /api/quotes` returning `503` → exactly **one** attempt → `AppError(503)` (non-idempotent never retried) |
| — | **Loading / empty (UI)** | The list renders `quotes-list`'s loading and empty states (inherited from Day-13/14). These are UI states, not pinned by the contract test — verify them by running the app live |
| 7 | **4xx friendly message reaches the UI** | The list **and** detail error branches now render `listErrorMessage()`/`detailErrorMessage()` = the mapped `AppError.message`, so a list `400` shows the server's ProblemDetails detail, not a hardcoded "is the API down?" string |

> **Fix applied after the strict review:** the list/detail error branches previously rendered a *hardcoded* string, discarding the mapped `AppError.message` — so a read-path 4xx never surfaced the friendly server message (only the create/auth paths did). They now read the typed error via `computed` `listErrorMessage()`/`detailErrorMessage()` ([quotes-list.component.ts](src/app/quotes-list/quotes-list.component.ts)), with a defensive fallback since `httpResource.error()` is typed `unknown`.

### One concrete thing the agent got wrong — and made it fix

**It assumed the 4xx was `ValidationProblemDetails` with an `errors` map.** The first-pass `toAppError` read the ASP.NET *default* validation shape:

```typescript
// agent's first cut — WRONG for this API
const firstError = err.error.errors[Object.keys(err.error.errors)[0]][0];
return new AppError(err.status, firstError);
```

But this API has **no DataAnnotations validation filter** on its minimal-API endpoints — its `400` comes from `ExceptionMiddleware` catching a `DomainException` and writing a **plain** `ProblemDetails` with a `detail` string and **no `errors` map** ([ExceptionMiddleware.cs:29-37](QuotesApi/Middleware/ExceptionMiddleware.cs#L29-L37)). Against the real response, `err.error.errors` is `undefined` → the agent's code throws / yields a blank message, so the user would see **nothing** on a 400. I caught it by reading the middleware, and rewrote the mapper to **prefer `detail`**, treating the `errors` map as a *tolerated fallback* for if the API ever adds validation. Test 3 pins the corrected behaviour (`message === the server detail`).

### What breaks if the API contract changes

- **`Quote` field renamed (`text` → `body`):** the characterization test still passes (it mocks the server with the *agreed* shape — it's a client-side regression guard, **not** a live contract check). Production would render blank. Closing that gap needs a live/Pact test against `:5075`; the spec here pins what the client *believes*, which catches client-side drift, not server drift.
- **API starts returning `ValidationProblemDetails` (adds `errors`):** already handled — `toAppError` surfaces the first field message. No change needed.
- **`400` stops including `detail` (title only):** `toAppError` falls back to the status-based friendly message ("The request was invalid."). No blank.
- **A GET endpoint becomes non-idempotent, or `429` adds a `Retry-After`:** the GET-only retry would now repeat a side-effecting call; and the fixed exponential backoff ignores `Retry-After`. Both are follow-ups if the contract moves that way.
- **A new transient code (e.g. `408`) should retry:** it currently won't — `isTransient` only covers `0`/`429`/`≥500`; add `408` to the predicate.

---

## How to run

```bash
# Characterization test (no API needed — HttpTestingController)
cd Day-15/piece-1 && npm install && npx ng test --watch=false

# Full app against the live API
cd Day-15/piece-1/QuotesApi && dotnet run     # → :5075
cd Day-15/piece-1 && npm start                # → :4200  (/api → :5075)
```
