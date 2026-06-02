import { Injectable, computed, signal } from '@angular/core';
import { httpResource }                  from '@angular/common/http';
import { CollectionDetail, Quote }       from './models/quote';

// Injectable service that loads a collection from the REAL Week-1 API and
// exposes it as signals.
//
// Day-13 is signals-first, so this uses Angular's httpResource() rather than
// HttpClient + Observable + subscribe.  httpResource:
//   • fires the request as soon as it is created (in an injection context)
//   • re-fetches automatically whenever a signal read in its URL factory
//     changes (here: collectionId)
//   • exposes .value() / .isLoading() / .error() / .status() as SIGNALS,
//     so the component reads them directly with no async pipe and no manual
//     subscription teardown
//
// inject() pattern: components consume this with
//   private readonly quotesService = inject(QuotesService);
@Injectable({ providedIn: 'root' })
export class QuotesService {

  // Which collection to show.  A writable signal so changing it re-triggers
  // the httpResource fetch.  Defaults to collection 1 (seeded by Day-11/12).
  readonly collectionId = signal<number>(1);

  // ── The real API call ─────────────────────────────────────────────
  // GET /api/collections/{id}  →  proxied to http://localhost:5075 by the
  // Angular dev server (proxy.conf.json).  Returns CollectionDetailReadModel.
  // The URL factory reads collectionId(), so writing collectionId.set(2)
  // automatically issues a fresh request.
  readonly collection = httpResource<CollectionDetail>(
    () => `/api/collections/${this.collectionId()}`,
  );

  // ── Signal projections the component consumes ─────────────────────
  // quotes(): the list, or [] while loading / on error.  Components derive
  // their computed() filters from this exactly as before — the only change
  // is that the data now originates from the live API instead of a fixture.
  readonly quotes = computed<Quote[]>(() => this.collection.value()?.quotes ?? []);

  // Collection name for the header (empty until the first response arrives).
  readonly collectionName = computed<string>(() => this.collection.value()?.name ?? '');

  // Pass-through of the resource's own state signals so the component can
  // render loading / error / empty / loaded branches with @if / @switch.
  readonly isLoading = this.collection.isLoading;
  readonly error     = this.collection.error;

  // Allow the UI to switch collections (re-fetch happens automatically).
  loadCollection(id: number): void {
    this.collectionId.set(id);
  }
}
