# Day 16 · Piece 1 — Routing, lazy loading, functional guard, View Transitions

Routed rebuild of the Day-15 app: **lazy-loaded** feature routes, a **functional auth guard**,
a **route param** (`/quotes/:id` = the real `Quote.id`), and a **View Transition** between the
quotes list and a quote detail. Built against the real Week-1 API; directed by an agent and verified.

---

## (1) The brief I gave the agent

> Convert `Day-16/piece-1` (currently a single-page app) to a routed app against my **real** Week-1 API. **Don't hand-type a giant route table — but I'll read every line.**
>
> **Real endpoints / param:**
> - List: `GET /api/quotes?page=N&size=N` → `Quote[]` (`{id, author, text, createdAt, isDeleted, ownerId}`).
> - Detail: `GET /api/quotes/{id}` → `Quote` (200) or `404`. **The route param is the `Quote.id` — an `int`** (the server route is constrained `/{id:int}`).
> - Create: `POST /api/quotes` — requires the `can-edit-quotes` (writer) token.
>
> **Build:**
> 1. **Lazy** routes via `loadComponent`: `/quotes` (list), `/quotes/:id` (detail), `/quotes/new` (create).
> 2. A **functional `CanActivateFn`** guarding `/quotes/new` (create needs auth) — redirect unauthenticated users to `/quotes`, preserving `returnUrl`. Reading is public, so don't guard the detail route.
> 3. Bind `:id` to the detail component via `withComponentInputBinding()`; **validate it's a positive int** before fetching (the server route is `int`-constrained — don't fire `GET /api/quotes/abc`).
> 4. A **View Transition** between list and detail (`withViewTransitions()`).
> 5. Keep search/filter/pagination on the list; keep the typed-error + interceptor stack from Day-15. No `any`.

---

## (2) The agent's output

### Route config — `src/app/app.routes.ts` (all lazy)

```typescript
export const routes: Routes = [
  { path: '', redirectTo: 'quotes', pathMatch: 'full' },

  { path: 'quotes', title: 'Quotes',
    loadComponent: () => import('./quotes-list/quotes-list.component').then(m => m.QuotesListComponent) },

  { path: 'quotes/new', title: 'New quote',
    canActivate: [authGuard],
    loadComponent: () => import('./quote-form-signals/quote-form-signals.component').then(m => m.QuoteFormSignalsComponent) },

  { path: 'quotes/:id', title: 'Quote detail',
    loadComponent: () => import('./quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent) },

  { path: '**', redirectTo: 'quotes' },
];
```
Order is load-bearing: **`quotes/new` is declared before `quotes/:id`** so the literal segment wins (see Bug 1). Wired in [app.config.ts](src/app/app.config.ts): `provideRouter(routes, withComponentInputBinding(), withViewTransitions())`.

### Functional guard — `src/app/guards/auth.guard.ts`

```typescript
export const authGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  return router.createUrlTree(['/quotes'], { queryParams: { returnUrl: state.url } });
};
```

### Lazy detail route — `src/app/quote-detail/quote-detail.component.ts`

```typescript
export class QuoteDetailComponent {
  private readonly quotes = inject(QuotesService);
  readonly id = input.required<string>();                 // ← :id via withComponentInputBinding()

  // Quote.id is a positive int; reject anything else WITHOUT hitting the API
  // (the server route is /{id:int} — GET /api/quotes/abc would just 404).
  protected readonly parsedId  = computed<number | null>(() => {
    const n = Number(this.id());
    return Number.isInteger(n) && n > 0 ? n : null;
  });
  protected readonly invalidId = computed(() => this.parsedId() === null);

  protected readonly quote   = this.quotes.selectedQuote;
  protected readonly loading = this.quotes.detailLoading;
  protected readonly errorMessage = computed(() => {
    const e = this.quotes.detailError();
    return e instanceof AppError ? e.message : 'Could not load this quote.';
  });

  constructor() {
    effect(() => {                                        // re-fetch when :id changes
      const n = this.parsedId();
      if (n !== null) this.quotes.selectQuote(n);         // service aborts any in-flight detail req
    });
  }

  protected state(): 'loading' | 'error' | 'loaded' {
    if (this.loading())            return 'loading';
    if (this.quotes.detailError()) return 'error';
    if (this.quote())              return 'loaded';
    return 'loading';
  }
}
// template: back-link + @if(invalidId) … @else @switch(state) loading/error/loaded;
// the .detail-card has `view-transition-name: quote-detail-card` for the animation.
```

The list rows are now `routerLink`s — opening a quote is a navigation, which lazy-loads the detail chunk + animates:
```html
@for (quote of filteredQuotes(); track quote.id) {
  <li><a class="quote-row" [routerLink]="['/quotes', quote.id]"> … </a></li>
}
```

---

## (3) Verification log

`npx ng test --watch=false` → **8/8 passing** (6 contract + **2 guard**). Prod build clean (316 kB initial / 84 kB transfer).

### Lazy loading proven by build output + runtime network capture

```
Initial chunk files                 main.js  20.86 kB     ← shell only; no feature views
Lazy chunk files
  chunk-…  quote-detail-component     9.56 kB             ← /quotes/:id  (loads on first open)
  chunk-…  quotes-list-component     17.18 kB             ← /quotes
  chunk-…  quote-form-signals-component 88.18 kB          ← /quotes/new
```

`main.js` carries none of the feature views. Runtime confirmation: clicking a quote row on a
hard-refreshed page fetches `chunk-…quote-detail-component.js` exactly once; subsequent navigations
serve it from cache with no new network request.

### Screenshot evidence (all six)

**1 · Lazy chunk fetched on first detail navigation — Network tab**
The `quote-detail-component.js` chunk is requested only when a quote row is first clicked, not in the initial page load.
![Network tab showing the detail chunk being fetched on first navigation](Screenshots/network-lazy-load-detail-route.png)

**2 · First detail navigation — chunk loaded, detail card rendered**
`/quotes/:id` resolves, the lazy chunk loads, `GET /api/quotes/{id}` returns 200, and the detail card renders.
![First detail navigation with the lazy chunk loaded and detail card shown](Screenshots/lazy-load-first-detail-navigation.png)

**3 · Second detail navigation — chunk served from cache (no refetch)**
Navigating to another quote re-uses the already-loaded chunk; no new chunk request appears in the Network tab.
![Second detail navigation served from cache with no chunk refetch](Screenshots/lazy-load-second-navigation-no-refetch.png)

**4 · Guard redirect — unauthenticated**
Signed out, navigating to `/quotes/new` is redirected by `authGuard` to `/quotes?returnUrl=%2Fquotes%2Fnew`.
![Auth guard redirecting an unauthenticated user away from /quotes/new](Screenshots/guard-redirect-unauthenticated.png)

**5 · Guard pass — authenticated (writer)**
Signed in as a writer, the same navigation to `/quotes/new` is allowed and the create form loads.
![Auth guard allowing an authenticated writer into /quotes/new](Screenshots/guard-pass-authenticated.png)

**6 · Invalid route param — no API call fired**
Navigating to `/quotes/abc` fails `parsedId` validation (`Number.isInteger && > 0`); the component shows the invalid-id message and **no `GET /api/quotes/abc` is sent** (empty Network tab).
![Invalid route param abc rejected client-side with no API call](Screenshots/invalid-param-no-api-call.png)

### States / edges exercised

| Edge | How | Result |
|---|---|---|
| **Guard redirect** | signed out → navigate to `/quotes/new` | redirected to `/quotes?returnUrl=%2Fquotes%2Fnew` (screenshot 4) |
| **Guard pass** | signed in (writer) → navigate to `/quotes/new` | create form loads at `/quotes/new` (screenshot 5) |
| **Lazy chunk — first load** | hard-refresh + click a quote row | `quote-detail-component.js` chunk fetched once (screenshots 1, 2) |
| **Lazy chunk — cached** | navigate to a second quote | chunk served from cache, no refetch (screenshot 3) |
| **Valid param** | `/quotes/5` | `parsedId()=5` → `GET /api/quotes/5` → 200 → detail card (screenshot 2) |
| **Invalid param** | `/quotes/abc` | `parsedId()=null` → **no request fired** → `"abc" is not a valid quote id.` (screenshot 6) |
| **Missing param (404)** | `/quotes/999999` | valid int → `GET /api/quotes/999999` → `404` → mapped `AppError` → friendly error |
| **View Transition** | list ↔ detail navigation | `withViewTransitions()` cross-fades; `.detail-card` morphs via `view-transition-name` |

### Bug 1 — Route order: `quotes/:id` declared before `quotes/new` (caught and fixed)

**The agent's first config listed the parameterised route first:**

```typescript
{ path: 'quotes/:id', loadComponent: … detail … },   // ← greedy
{ path: 'quotes/new', loadComponent: … create … },   // ← never reached
```

Angular matches top-down, so navigating to **/quotes/new** matched `quotes/:id` with `id = "new"` → it loaded the **detail** component, which tried to resolve `"new"` as a `Quote.id`. Against the real API that's nonsense — `Quote.id` is an **`int`** and the server route is `/api/quotes/{id:int}`, so `"new"` is never a valid id; the create form was unreachable. I caught it by clicking "＋ New quote" and landing on a detail view, and fixed it by **ordering the literal `quotes/new` before `quotes/:id`** (now documented with a comment). The detail component's `parsedId` validation is the second line of defence — it rejects `"new"`/`"abc"` without firing a request.

### Bug 2 — Token in-memory only: guard always failed on address-bar navigation

The `AuthService` inherited from Day-14 stored the token in a plain signal (`signal<string | null>(null)`) with no persistence. Angular's in-memory signal survives SPA navigation (routerLink), but **typing a URL in the address bar triggers a full page reload** — the app reinitialises, the signal resets to `null`, and `isAuthenticated()` returns `false` even though the user had just signed in.

The guard was correct; the backing store was wrong. Fix: persist token + email to `sessionStorage` on login and read them back on service construction. `sessionStorage` clears when the tab closes, so there is no cross-session token leak.

```typescript
// Before (Day-14 carry-forward — in-memory only)
readonly token = signal<string | null>(null);

// After (Day-16 fix — survives page reload within the same tab)
readonly token = signal<string | null>(sessionStorage.getItem('auth_token'));

async login(email: string, password: string): Promise<void> {
  const res = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { email, password }));
  sessionStorage.setItem('auth_token', res.accessToken);
  sessionStorage.setItem('auth_email', email);
  this.token.set(res.accessToken);
  this.email.set(email);
}

logout(): void {
  sessionStorage.removeItem('auth_token');
  sessionStorage.removeItem('auth_email');
  this.token.set(null);
  this.email.set(null);
}
```

Guard test unchanged — `provideRouter([])` + signal mock still pins both outcomes. The fix is in the service, not the guard logic.

### What breaks if the detail route or id field changes

- **`Quote.id` becomes a GUID/string (auth migration):** the routerLink `['/quotes', quote.id]` still builds, but the detail component's `parsedId` (`Number.isInteger(n) && n > 0`) would reject every GUID as "invalid id" → **detail never loads**. Fix = relax `parsedId` to accept the new id format.
- **Detail path moves (`/api/quotes/{id}` → `/api/quotes/by-id/{id}`):** `QuotesService.detailResource` builds `/api/quotes/${id}` → every detail fetch `404`s → friendly error for all quotes. Fix = update the one URL template.
- **List stops returning `id` (renamed to `quoteId`):** `routerLink` gets `undefined` → links to `/quotes/undefined` → `parsedId` null → "invalid id" for every row. Caught only when you click a row.
- **`/quotes/:id` gains a required query (e.g. `?include=author`):** current navigation omits it → server may 400; the route param alone no longer fully addresses a quote.

---

## How to run

```bash
cd Day-16/piece-1 && npm install && npx ng test --watch=false   # 8/8, no API needed
cd Day-16/piece-1/QuotesApi && dotnet run                        # → :5075
cd Day-16/piece-1 && npm start                                   # → :4200
# /quotes → sign in → click ＋ New quote → /quotes/new (guard pass)
# sign out → navigate to /quotes/new → /quotes?returnUrl=… (guard redirect)
# click a quote row → /quotes/:id (lazy chunk + View Transition)
# type /quotes/abc → "abc" is not a valid quote id (no API call)
```
