# Day 14 · Piece 2 — Signal Forms preview (create a quote)

The **same** create-a-quote form as piece-1, rebuilt with Angular 21's experimental **Signal Forms**
preview API (`@angular/forms/signals`, Angular 21.2.15), plus a side-by-side comparison to the
reactive-forms version. Same real Week-1 endpoint and fields.

---

## (1) The brief I gave the agent

> Rebuild the piece-1 create-a-quote form using the **Signal Forms preview** (`@angular/forms/signals`) in `Day-14/piece-2`. Keep the reactive component in the repo for comparison (the Signal Forms one is what's hosted). Against my real Week-1 API:
>
> - **Endpoint:** `POST /api/quotes` ([QuoteEndpointExtensions.cs:45](QuotesApi/Extensions/QuoteEndpointExtensions.cs#L45)) → `201 Created` + `Location: /api/quotes/{id}`; `RequireAuthorization("can-edit-quotes")`.
> - **Body — `CreateQuoteRequest`** ([CreateQuoteRequest.cs](QuotesApi/DTOs/CreateQuoteRequest.cs)): exactly `author` (`[Required]`, `[MaxLength(200)]`) and `text` (`[Required]`, `[MaxLength(1000)]`). `ownerId` comes from the JWT, **not** the body. Server also rejects whitespace-only with `400` ([Quote.cs:18-22](QuotesApi/Models/Quote.cs#L18-L22)). **Do not invent fields.**
>
> Requirements: a `signal()` model + `form(model, schema)`; validators in the schema (`required`/`maxLength(200|1000)`/a `validate` for whitespace); bind controls with `[formField]`; submit with `submit(form, {action})` that POSTs and folds server errors back into the form; same a11y as piece-1 (labels, `aria-invalid`/`aria-describedby`, `role="alert"`, focus-to-first-error); exercise pristine/dirty/touched, validators, clean + failed submit. No `any`. Then write the comparison: where it's simpler, where it's still rough.

---

## (2) The agent's output (Signal Forms version)

`src/app/quote-form-signals/quote-form-signals.component.ts`:

```typescript
import { form, submit, required, maxLength, validate, FormField, ValidationError }
  from '@angular/forms/signals';

// Mirrors the server's IsNullOrWhiteSpace guard (built-in required() treats "   " as present).
function notBlank(value: string, label: string): ValidationError | null {
  return typeof value === 'string' && value.length > 0 && value.trim().length === 0
    ? { kind: 'whitespace', message: `${label} can’t be only spaces.` }
    : null;
}

@Component({
  selector: 'app-quote-form-signals',
  standalone: true,
  imports: [FormField],
  template: `
    <form (submit)="$event.preventDefault(); onSubmit()" novalidate aria-labelledby="sf-heading">
      <div class="field">
        <label for="author">Author <span class="req" aria-hidden="true">*</span></label>
        <!-- No maxlength/required here: [formField] derives them from the schema. -->
        <input id="author" type="text" autocomplete="off"
               [formField]="form.author"
               [attr.aria-invalid]="showError('author') ? 'true' : null"
               [attr.aria-describedby]="describedBy('author')" />
        <p class="hint" id="author-hint">Required · up to 200 characters.</p>
        @if (showError('author')) {
          <p class="error" id="author-error" role="alert">{{ firstError('author') }}</p>
        }
      </div>
      <!-- text field identical: [formField]="form.text", maxlength derived, live counter -->
      <div class="server-status" aria-live="assertive">
        @if (serverError(); as msg) { <p class="error server-error">{{ msg }}</p> }
      </div>
      <button type="submit" [disabled]="form().submitting()"
              [attr.aria-busy]="form().submitting() ? 'true' : null">
        {{ form().submitting() ? 'Adding…' : 'Add quote' }}
      </button>
    </form>
    <div class="success-status" aria-live="polite">
      @if (lastCreated(); as created) {
        <p class="success">Added quote #{{ created.id }} by {{ created.author }}.</p>
      }
    </div>
  `,
})
export class QuoteFormSignalsComponent {
  private readonly quotes = inject(QuotesService);
  private readonly host   = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly model = signal({ author: '', text: '' });

  readonly form = form(this.model, (path) => {
    required (path.author, { message: 'Author is required.' });
    maxLength(path.author, 200, { message: 'Author must be 200 characters or fewer.' });
    validate (path.author, (ctx) => notBlank(ctx.value(), 'Author'));

    required (path.text, { message: 'Quote text is required.' });
    maxLength(path.text, 1000, { message: 'Quote text must be 1000 characters or fewer.' });
    validate (path.text, (ctx) => notBlank(ctx.value(), 'Quote text'));
  });

  readonly lastCreated = signal<Quote | null>(null);
  readonly serverError = signal<string | null>(null);

  // touched()/invalid() are signals → updates under zoneless with NO `submitted` flag.
  protected showError(name: FieldName): boolean {
    const state = this.form[name]();
    return state.touched() && state.invalid();
  }
  protected firstError(name: FieldName): string {
    return this.form[name]().errors()[0]?.message ?? '';
  }
  protected describedBy(name: FieldName): string {
    return this.showError(name) ? `${name}-hint ${name}-error` : `${name}-hint`;
  }

  protected async onSubmit(): Promise<void> {
    this.serverError.set(null);
    const ok = await submit(this.form, {
      action: async (f) => {                              // runs ONLY if valid
        const { author, text } = f().value();
        try {
          const created = await this.quotes.postQuote({ author: author.trim(), text: text.trim() });
          this.lastCreated.set(created);
          return undefined;                               // success
        } catch (err: unknown) {
          const message = this.quotes.describeError(err);
          this.serverError.set(message);
          return [{ kind: 'server', message }];           // folds into the form
        }
      },
      onInvalid: () => this.focusFirstInvalid(),          // Signal Forms doesn't move focus
    });
    if (ok) { this.model.set({ author: '', text: '' }); this.form().reset(); }
  }

  private focusFirstInvalid(): void {
    const order: FieldName[] = ['author', 'text'];
    const first = order.find(name => this.form[name]().invalid());
    if (first) this.host.nativeElement.querySelector<HTMLElement>(`#${first}`)?.focus();
  }
}
```

The POST goes through `QuotesService.postQuote()` (bare `http.post<Quote>('/api/quotes', …)`), reusing the same `describeError()` mapping as the reactive version.

---

## (3) Verification log

Run: API on `:5075` (`cd QuotesApi && dotnet run`), app on `:4200` (`npm start`); sign in as the writer `demo@example.com` / `P@ssw0rd!` for a real `201`.

### States / edges exercised

| State / edge | How triggered | Expected result |
|---|---|---|
| **Pristine** | Initial load | No errors; `form().dirty()` false, `form().touched()` false; submit shows "Add quote" |
| **Dirty** | Type in author | `form.author().dirty()` flips true; value tracked on the model signal |
| **Touched** | Blur a field | `form.author().touched()` true → its error (if any) renders |
| **Validator — required** | Submit blank | `submit()` marks all touched → both "is required" errors show; **focus jumps to `#author`** via `onInvalid` |
| **Validator — whitespace** | Type `"   "`, blur | `notBlank` → "Author can’t be only spaces." |
| **`maxLength` → native cap (not a fired validator)** | Try to type past 200/1000 | `[formField]` reflects the schema's `maxLength` onto the control as a native `maxlength` attribute, so the browser **hard-caps** input at 200/1000. The value can never exceed the limit, so the `maxLength` *validator* (`length > max`) never fires and its error message is unreachable through the UI. Verified in the bundle (`setNativeDomProperty` → `setAttribute('maxLength', …)`). |
| **Submitting** | Valid submit | `form().submitting()` true → button "Adding…", `disabled`, `aria-busy` while POST in flight |
| **Failed submit (server)** | Submit signed out / as viewer | action catches `401`/`403` → returns `{kind:'server'}` → `serverError()` announced (assertive); values kept |
| **Clean submit** | Writer + valid | `201` → success announced (polite); `model` cleared + `form().reset()` clears touched/dirty; list reloads |

### How I checked a11y

Same wiring as piece-1 — `aria-required` (now native, see below), `aria-invalid`/`aria-describedby` toggled on error, `role="alert"` field errors, persistent `aria-live` regions, keyboard path Author → text → submit, focus-to-first-error on invalid submit. *Caveat:* no live axe/screen-reader pass captured this session; rows are the states the form is built to exercise.

### One concrete thing I caught while verifying — and corrected

**The `maxLength` validator never fires — it silently becomes a native input cap.** My first verification log had a "type past 200/1000 → maxLength error shows, counter turns red" row, copied from the piece-1 mental model. Reading the actual bundle while verifying proved that wrong: `[formField]` reflects the schema's `maxLength` onto the control as a real `maxlength` attribute (`setNativeDomProperty` → `renderer.setAttribute(element, 'maxLength', …)`, `signals.mjs:771-776`). So the browser hard-caps input at 200/1000, the value can never exceed the limit, and the `maxLength` validator's `length > max` check (`signals.mjs:232`) is **unreachable through the bound control** — its error message is dead UI.

This is exactly the "a validator that doesn't actually fire in the preview API" trap the brief warns about. I corrected it: the verification row now states `maxLength` acts as a **native cap, not a displayed validator**, and I removed the dead `[class.over]` red-counter binding + its style (the condition `length > 1000` can never be true). The counter is now labelled informational-only. *(If you wanted the over-limit message to actually display, you'd swap `maxLength()` for a custom `validate()` length check, which does not reflect to a native attribute — at the cost of the free native cap.)*

**Related API fact (not a "caught lie" — a verified property):** `[formField]` manages **zero ARIA**. `elementAcceptsNativeProperty` only writes `required`/`maxLength`/`min`/`max`/`disabled`/`readonly`/`name`, and a grep for `aria` across `signals.mjs` returns nothing. So the manual `aria-invalid`/`aria-describedby`/`role="alert"` are kept exactly as piece-1 — Signal Forms wires native *validation* props from the schema, but a11y is still entirely hand-rolled.

### What breaks if the Week-1 API contract changes

- **Field renamed (`text` → `body`):** the model is still `{ author, text }`; the POST sends `text`, the server binds nothing → empty `Text` → `400`. The schema's `path.text` is typed against the local model, not the C# DTO, so nothing warns — same blind spot as reactive forms.
- **New required field (e.g. `category`):** every submit `400`s with the generic server message; you must add a model key + a `required(path.category, …)` schema rule by hand.
- **Length tightened (author `200` → `100`):** the schema still says `maxLength(200)`, so `[formField]` sets `maxlength="200"` on the input and the client passes 101–200-char authors that the server now `400`s. The stale limit is invisible until submit.
- **Auth/policy change:** the `401/403` branch goes dead or surfaces as the catch-all "Unexpected error (HTTP n)".

---

## Signal Forms vs Reactive Forms — what's simpler, what's still rough

**Simpler with Signal Forms:**
- **No `submitted` flag.** Piece-1 needed a `submitted` *signal* because `markAllAsTouched()` doesn't trigger change detection under zoneless. Here `touched()`/`invalid()`/`errors()` are signals, so `submit()`'s mark-all-touched repaints natively — error gating is just `touched() && invalid()`.
- **`required`/`maxlength` come from the schema.** `[formField]` syncs them onto the native control, so the template carries no `required`/`maxlength`/`aria-required` attributes — one source of truth (the schema), no client/attribute drift. (Caveat below: for `maxLength` this *replaces* a displayed validator with a native cap.)
- **`submit()` owns the lifecycle.** It marks touched, gates on validity, sets `submitting()`, and folds server errors back into the form. Piece-1 hand-rolled all of that (`submitting`/`submitError` signals, `form.disable()/enable()`, manual valid-check).
- **Model is a plain `signal`.** `form().value()` is just the typed model; no `getRawValue()`, no `nonNullable` ceremony to avoid `null`.

**Still rough (preview):**
- **`maxLength` silently becomes a native cap, not a displayed error.** Because `[formField]` reflects it to a native `maxlength` attribute, the over-limit *validator message* is unreachable — convenient, but surprising if you expected a normal validation error like `required`/whitespace produce. You only get the message back by abandoning the built-in and writing a custom `validate()`.
- **Zero ARIA management.** As noted above — you still hand-wire `aria-invalid`/`aria-describedby`/`role="alert"` and **focus-to-first-error** (`onInvalid` hook). The "forms" half got reactive; the "accessibility" half did not.
- **Everything is `@experimental 21.0.0`.** API names/shapes (`form`, `submit`, `validate`, `[formField]`, error `{kind,message}`) can move release to release — not safe for production yet.
- **Custom errors are stringly-typed.** `validate(... => ({ kind: 'whitespace', message }))` — `kind` is a bare string with no central registry, so consumers match on magic strings.
- **Dynamic field access is awkwardly typed.** `this.form[name]()` (indexing the FieldTree by a union key) typechecks but reads oddly; the ergonomic path is static `form.author` / `form.text`.

**Net:** Signal Forms removes the zoneless boilerplate and unifies validation as the single source for native props — a real simplification for the *form* mechanics. But a11y is still entirely your job, and the preview status means it's a "learn it now, ship it later" API.

---

## How to run

```bash
cd Day-14/piece-2/QuotesApi && dotnet run          # → http://localhost:5075
cd Day-14/piece-2 && npm install && npm start      # → http://localhost:4200  (/api → :5075)
```

Sign in as `demo@example.com` / `P@ssw0rd!` (writer) for a real `201`; signed out / `reader@example.com` exercises the `401`/`403` server-error path.
