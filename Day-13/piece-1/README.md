# Day 13 · Piece 1 — Signals + Zoneless + Standalone

Angular 21 standalone app — no NgModules, signals-first state, zoneless change detection, new control flow syntax, `inject()` over constructor injection.

---

## Folder structure

```
src/
├── main.ts                                     ← bootstrapApplication (no NgModule)
├── index.html
├── styles.css
└── app/
    ├── app.config.ts                           ← provideZonelessChangeDetection()
    ├── app.component.ts                        ← root standalone component
    ├── quotes.service.ts                       ← signal store (inject()-ready)
    ├── models/
    │   └── quote.ts                            ← Quote interface
    └── collection-viewer/
        └── collection-viewer.component.ts      ← THE EXERCISE COMPONENT
```

---

## Exercise deliverables

### Standalone component — `CollectionViewerComponent`

[`src/app/collection-viewer/collection-viewer.component.ts`](src/app/collection-viewer/collection-viewer.component.ts)

The component derives `filteredQuotes` — a `computed()` value — from **two signals**: `searchTerm` and `selectedAuthor`. Both signal writes happen from the template controls; every write triggers a targeted re-render of the `@for` list without a full component tree traversal.

```typescript
@Component({
  selector: 'app-collection-viewer',
  standalone: true,
  imports: [FormsModule],
  template: `...`,
})
export class CollectionViewerComponent {

  // inject() — no constructor parameter needed
  private readonly quotesService = inject(QuotesService);

  // Signal 1 — text search
  readonly searchTerm     = signal<string>('');

  // Signal 2 — author filter
  readonly selectedAuthor = signal<string>('');

  // computed() — derived from BOTH signals (and the service's quote list signal)
  // Re-evaluated lazily whenever any of the three dependencies changes.
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

  // effect() — side effect that fires whenever filteredQuotes changes
  private readonly filterEffect = effect(() => {
    const count = this.filteredQuotes().length;   // reads the signal → registers dependency
    this.lastFilterChange.set(new Date().toLocaleTimeString());
  });
}
```

### `@for` list with `track`

```html
@for (quote of filteredQuotes(); track quote.id) {
  <li class="quote-card">
    @switch (authorTier(quote.author)) {
      @case ('stoic')   { <span class="tag">Stoic</span> }
      @default          { <span class="tag">Philosopher</span> }
    }
    <p class="text">"{{ quote.text }}"</p>
    <p class="meta">— {{ quote.author }}</p>
  </li>
}
```

`track quote.id` is mandatory — Angular uses the identity expression to reconcile the DOM on re-render. Without it, every signal write would destroy and recreate every list node; with it, only changed items are patched.

### `@if` conditional

```html
@if (filteredQuotes().length > 0) {
  <ul class="quote-list"> ... </ul>
} @else {
  <p class="empty">No quotes match the current filters.</p>
}
```

### One line: what zoneless changes about change detection

**Zoneless removes zone.js's speculative polling — Angular only re-renders a view when a signal it reads has been written to, making every change-detection pass targeted and eliminating the need to check the whole component tree.**

---

## Signal primitives used

| Primitive | Where | Purpose |
|---|---|---|
| `signal<T>()` | `searchTerm`, `selectedAuthor`, `lastFilterChange` in component; `quotes` in service | Writable reactive state |
| `computed<T>()` | `filteredQuotes`, `totalCount`, `authors` | Derived state from one or more signals; lazily re-evaluated |
| `effect()` | `filterEffect` | Side effect (log update) that re-runs when `filteredQuotes` changes |
| `inject()` | `quotesService` field | DI without constructor parameter |

---

## Angular 21 features used

| Feature | Usage |
|---|---|
| `standalone: true` | Every component — no NgModule anywhere |
| `provideZonelessChangeDetection()` | `app.config.ts` — replaces zone.js |
| `bootstrapApplication()` | `main.ts` — standalone bootstrap |
| `@for ... track` | Quote list rendering |
| `@if / @else` | Empty-state guard |
| `@switch / @case / @default` | Author tier badge |
| `inject()` | `QuotesService` resolution in field initialiser |

---

## How to run

```bash
cd Day-13/piece-1
npm install
npm start
# → http://localhost:4200
```

Type in the search box — `searchTerm` signal updates → `filteredQuotes` computed re-evaluates → `@for` list re-renders. Select an author from the dropdown — `selectedAuthor` signal updates → same path fires again. Both signals contribute to the same `computed()` value; either one changing is sufficient to trigger a re-render.

The `effect()` log at the bottom updates its timestamp each time `filteredQuotes` changes, proving the effect is reactive to the computed value.
