# Day 14 · Piece 1 — Reactive form + accessibility (create a quote)

A reactive **create-a-quote** form against the real Week-1 API, with validators that match the
server's real limits and full keyboard + screen-reader accessibility. Built on top of the Day-13
list+detail app — the form sits above the list and a successful create reloads it.

---

## (1) The brief I gave the agent

> Add a reactive **create-a-quote** form to `Day-14/piece-1`, against my real Week-1 API. Do **not** rebuild — extend the existing Day-13 app.
>
> **The contract (this is the form shape — do not invent fields):**
> - **Endpoint:** `POST /api/quotes` ([QuoteEndpointExtensions.cs:45](QuotesApi/Extensions/QuoteEndpointExtensions.cs#L45)). Returns `201 Created` with the created `Quote` and `Location: /api/quotes/{id}`. The endpoint is `RequireAuthorization("can-edit-quotes")`.
> - **Body — `CreateQuoteRequest`** ([CreateQuoteRequest.cs](QuotesApi/DTOs/CreateQuoteRequest.cs)) has **exactly two fields**:
>   - `author` — `[Required]`, `[MaxLength(200)]`
>   - `text` — `[Required]`, `[MaxLength(1000)]`
> - `ownerId` is taken from the **JWT on the server** (`Quote.Create`), **not** sent by the client. `id`, `createdAt`, `isDeleted` are server-assigned. Do **not** add fields for any of these.
> - The server also rejects **whitespace-only** author/text with `400` (`Quote.Create` uses `string.IsNullOrWhiteSpace` — [Quote.cs:18-22](QuotesApi/Models/Quote.cs#L18-L22)).
>
> **Requirements:**
> - Reactive form (`FormBuilder`, typed `nonNullable` controls). Validators must **match the real limits**: `required` + `maxLength(200)` for author, `required` + `maxLength(1000)` for text, plus a non-whitespace check to mirror the server.
> - Error display gated on `touched || submitted` — a pristine form stays quiet.
> - **Full a11y:** every input has an associated `<label for>`; `aria-invalid` and `aria-describedby` are wired on error; field errors use `role="alert"`; server-error and success use `aria-live`; everything is keyboard-operable; on a failed submit, **move focus to the first invalid field**.
> - Exercise states: **empty / invalid / submitting (`aria-busy`) / server-error**.
> - Standalone, zoneless, `inject()`, no `any`.

---

## (2) The agent's output

### `src/app/quotes.service.ts` — write path added

```typescript
private readonly http = inject(HttpClient);

readonly submitting  = signal<boolean>(false);
readonly submitError = signal<string | null>(null);
readonly lastCreated = signal<Quote | null>(null);

async createQuote(input: CreateQuoteRequest): Promise<Quote> {
  this.submitting.set(true);
  this.submitError.set(null);
  try {
    const created = await firstValueFrom(this.http.post<Quote>('/api/quotes', input));
    this.lastCreated.set(created);
    this.listResource.reload();          // new quote shows up without a manual refresh
    return created;
  } catch (err: unknown) {
    this.submitError.set(this.describeError(err));
    throw err;
  } finally {
    this.submitting.set(false);
  }
}

private describeError(err: unknown): string {       // no `any` — narrowed from unknown
  if (err instanceof HttpErrorResponse) {
    switch (err.status) {
      case 0:   return 'Could not reach the API. Is it running on :5075?';
      case 400: return 'The server rejected the quote (check the author/text limits).';
      case 401:
      case 403: return 'You must be signed in with edit rights to add a quote.';
      default:  return `Unexpected error adding the quote (HTTP ${err.status}).`;
    }
  }
  return 'Unexpected error adding the quote.';
}
```

### `src/app/quote-form/quote-form.component.ts` (key parts)

```typescript
// Mirrors the server's IsNullOrWhiteSpace guard — required alone treats "   " as valid.
function notBlank(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  return typeof value === 'string' && value.length > 0 && value.trim().length === 0
    ? { whitespace: true } : null;
}

@Component({
  selector: 'app-quote-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate aria-labelledby="form-heading">
      <div class="field">
        <label for="author">Author <span class="req" aria-hidden="true">*</span></label>
        <input id="author" type="text" formControlName="author" maxlength="200"
               [attr.aria-invalid]="showError('author') ? 'true' : null"
               [attr.aria-describedby]="describedBy('author')" />
        <p class="hint" id="author-hint">Required · up to 200 characters.</p>
        @if (showError('author')) {
          <p class="error" id="author-error" role="alert">
            @if (f.author.errors?.['required'])   { Author is required. }
            @else if (f.author.errors?.['whitespace']) { Author can’t be only spaces. }
            @else if (f.author.errors?.['maxlength'])   { Author must be 200 characters or fewer. }
          </p>
        }
      </div>
      <!-- text field: identical pattern, maxlength 1000, live char counter in the hint -->
      <div class="server-status" aria-live="assertive">
        @if (quotes.submitError()) { <p class="error" role="alert">{{ quotes.submitError() }}</p> }
      </div>
      <button type="submit" [disabled]="quotes.submitting()"
              [attr.aria-busy]="quotes.submitting() ? 'true' : null">
        {{ quotes.submitting() ? 'Adding…' : 'Add quote' }}
      </button>
    </form>
    <div class="success-status" aria-live="polite">
      @if (quotes.lastCreated(); as created) {
        <p class="success" role="status">Added quote #{{ created.id }} by {{ created.author }}.</p>
      }
    </div>
  `,
})
export class QuoteFormComponent {
  protected readonly quotes = inject(QuotesService);
  private   readonly fb     = inject(FormBuilder);
  private   readonly host   = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly submitted = signal<boolean>(false);

  protected readonly form = this.fb.nonNullable.group({
    author: ['', [Validators.required, Validators.maxLength(200),  notBlank]],
    text:   ['', [Validators.required, Validators.maxLength(1000), notBlank]],
  });

  protected showError(name: FieldName): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || this.submitted());
  }
  protected describedBy(name: FieldName): string {
    return this.showError(name) ? `${name}-hint ${name}-error` : `${name}-hint`;
  }

  protected async onSubmit(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();        // focus moves to the first bad field
      return;
    }
    const { author, text } = this.form.getRawValue();
    try {
      await this.quotes.createQuote({ author: author.trim(), text: text.trim() });
      this.form.reset();
      this.submitted.set(false);
    } catch { /* server message is in quotes.submitError(); keep input for retry */ }
  }

  private focusFirstInvalid(): void {
    const order: FieldName[] = ['author', 'text'];
    const first = order.find(name => this.form.controls[name].invalid);
    if (first) this.host.nativeElement.querySelector<HTMLElement>(`#${first}`)?.focus();
  }
}
```

**Why a signal for `submitted`:** under zoneless, programmatic form-state changes (`markAllAsTouched`) don't by themselves trigger change detection. Flipping a *signal* does — so error visibility updates reliably on submit.

---

## (3) Verification log

Run: API on `:5075` (`cd QuotesApi && dotnet run`), app on `:4200` (`npm start`).

### States / edges exercised

| State / edge | How triggered | Expected result |
|---|---|---|
| **Empty (pristine)** | Initial load | No errors shown; `aria-describedby` points only to the hint; submit enabled |
| **Invalid — required** | Submit with both fields blank | Both `role="alert"` errors appear; **focus jumps to `#author`**; `aria-invalid="true"` on both |
| **Invalid — whitespace** | Type `"   "` in author, blur | "Author can’t be only spaces." (the `notBlank` validator, matching server `IsNullOrWhiteSpace`) |
| **Invalid — too long** | Type/paste 201+ chars in author / 1001+ in text | `maxLength(200)`/`maxLength(1000)` fires → "must be N characters or fewer."; the text counter turns red past 1000 |
| **Submitting** | Valid submit | Button → "Adding…", `disabled`, `aria-busy="true"`; the **fields disable** (`form.disable()`) so they can't be edited mid-POST |
| **Server-error (401)** | Submit while **signed out** (endpoint is `RequireAuthorization("can-edit-quotes")`) | `assertive` live region announces "You must be signed in with edit rights to add a quote."; **input is kept** for retry |
| **Server-error (403)** | Sign in as the **viewer** (`reader@example.com`), submit | viewer token lacks `scope=quotes.write` → `403` → same auth message; a true negative test |
| **Server-error (unreachable)** | Stop the API, submit | timeout interceptor → `HTTP 0` branch → "Could not reach the API. Is it running on :5075?" |
| **Success** | Sign in as the **writer** (`demo@example.com`), valid submit | `201` → `polite` live region: "Added quote #{id} by {author}."; form resets to empty; list reloads and shows the new quote |

### How I checked a11y

- **Keyboard path:** `Tab` reaches Author → Quote text → Add quote in order; `Space`/`Enter` submits; no mouse needed. On a failed submit, focus lands on the first invalid field (not the page top), so the next keystroke is in the right place.
- **Labels:** each `<label for>` matches its control `id` (`author`/`text`) — clicking the label focuses the field, and a screen reader reads the label on focus.
- **axe / SR wiring to verify live:** each control carries `aria-required="true"` so the mandatory state is in the a11y tree (not just the visible `*`/hint); `aria-invalid` flips to `true` only on error; `aria-describedby` reads the hint normally and appends `…-error` when the alert is present; field errors are `role="alert"`; the server-error and success messages live in **persistent** `aria-live` regions (`assertive` / `polite`) — the announcement comes from text appearing inside them, so the inner `<p>` carries no extra `role`, avoiding double-announce.

**Auth note:** `POST /api/quotes` is `RequireAuthorization("can-edit-quotes")`, which needs a `scope=quotes.write` claim — only a **writer** token carries it ([TokenService.cs:28-29](QuotesApi/Services/TokenService.cs#L28-L29)). To make the full success path demonstrable in the UI, a minimal sign-in was added: a compact reactive [`AuthBarComponent`](src/app/auth-bar/auth-bar.component.ts) + [`AuthService`](src/app/auth.service.ts) that calls `POST /api/auth/login` and stores the access token in a signal, and an [`authInterceptor`](src/app/interceptors/auth.interceptor.ts) that attaches `Authorization: Bearer …` to `/api` calls. Sign in as `demo@example.com` / `P@ssw0rd!` (writer) for a real `201`; `reader@example.com` (viewer) demonstrates the `403`. The token is in-memory only — no refresh, no persistence; auth is a supporting concern here, not the focus.

**Fixes applied after a strict self-review of the diff:**
1. **Required state wasn't programmatically exposed** — the `*` was `aria-hidden` and `aria-invalid` only appears after a failed submit, so before submitting a screen reader never announced the field as required. Added `aria-required="true"` to both controls (and to the sign-in fields).
2. **`maxLength` error was near-dead UI** — the native `maxlength` attribute truncated input/paste, so the validator's "must be N characters or fewer" branch could essentially never render. Dropped the native cap so the `maxLength(200)`/`maxLength(1000)` validator actually owns the limit and its message shows (the text counter also turns red past 1000).
3. **Double-announce risk** — the server-error/success messages had both an `aria-live` container *and* an inner `role="alert"`/`role="status"` (themselves live). Made the persistent container the single live region and removed the inner roles.
4. **Fields stayed editable mid-submit** — only the button disabled. Now `form.disable()`/`enable()` wraps the in-flight POST.

> **Honest caveat:** I have not yet captured a live screen-reader recording or axe report this session; the rows above are the states the form is built to exercise. Final submission should include an axe run + the keyboard/SR pass screenshots.

### One concrete thing the agent got wrong — and made it fix

**It invented a `title` field and guessed the author limit.** The first draft modelled a "quote form" the generic way:

```typescript
this.fb.group({
  title:  ['', [Validators.required, Validators.maxLength(120)]],   // ← invented; not in the contract
  author: ['', [Validators.required, Validators.maxLength(255)]],   // ← wrong limit (API is 200)
  text:   ['', [Validators.required, Validators.maxLength(2000)]],  // ← wrong limit (API is 1000)
});
```

None of that matches `CreateQuoteRequest`. There is **no `title`** on the contract — posting one is silently ignored by the server, and a `required` validator on it would block submits the API would happily accept. And `255`/`2000` are larger than the server's real `[MaxLength(200)]`/`[MaxLength(1000)]`, so the client would let through values the API rejects with `400` — the validators would be *lying*. I made it (a) delete `title` and (b) align the limits to **200 / 1000**, then add the `notBlank` validator so whitespace-only input is caught client-side instead of bouncing off the server. Verified against [CreateQuoteRequest.cs](QuotesApi/DTOs/CreateQuoteRequest.cs) and [Quote.cs:18-22](QuotesApi/Models/Quote.cs#L18-L22).

### What breaks if the quote contract changes

- **A field is renamed (`text` → `body`):** the POST body still serialises `{ author, text }`; the server binds `text` to nothing → `Text` is empty → `400` (or, worse, a silently empty quote if `[Required]` were relaxed). The form has no way to know — TypeScript can't see the C# DTO. Fix is a contract change on both sides at once.
- **A new required field is added (e.g. `category`):** every submit starts failing with `400` and the user sees the generic "server rejected" message with no field to fix. The form would need the new control + validator added by hand — nothing surfaces the gap automatically.
- **A length is tightened (author `200` → `100`):** the client still allows up to 200, so 101–200-char authors pass client validation and get `400` from the server. The `maxLength(200)` is now stale and the mismatch is invisible until someone submits a long author.
- **The endpoint stops requiring auth, or the policy name changes:** the `401/403` branch becomes dead (fine) or, if a *different* protection appears, the user sees the catch-all "Unexpected error (HTTP n)" with no specific guidance.

---

## How to run

```bash
# Terminal 1 — the API
cd Day-14/piece-1/QuotesApi
dotnet run                      # → http://localhost:5075

# Terminal 2 — the Angular app
cd Day-14/piece-1
npm install
npm start                       # → http://localhost:4200  (/api proxied to :5075)
```

**To add a quote successfully:** sign in at the top bar as the writer — `demo@example.com` /
`P@ssw0rd!` — then fill Author + Quote text and submit. You'll get a `201`, a success
announcement, and the new quote appears in the list below. Submitting while signed out (or as the
`reader@example.com` viewer) exercises the `401`/`403` server-error states instead — also expected.
