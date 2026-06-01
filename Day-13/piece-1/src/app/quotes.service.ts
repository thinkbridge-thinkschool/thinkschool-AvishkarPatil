import { Injectable, signal } from '@angular/core';
import { Quote }              from './models/quote';

// Injectable service that owns the canonical signal state for the quote list.
// Components consume the exposed WritableSignal directly — no Subject, no
// BehaviorSubject, no Observable needed for local state.
//
// inject() pattern: callers use
//   private readonly quotesService = inject(QuotesService);
// instead of constructor(private quotesService: QuotesService).
// Both wire DI identically; inject() works outside a constructor (in field
// initialisers, factory functions, etc.) which is more flexible.
@Injectable({ providedIn: 'root' })
export class QuotesService {

  // The signal is the single source of truth for the quote list.
  // It is WritableSignal<Quote[]> — the service owns writes;
  // consumers get a read-only view via quotes.asReadonly() if needed.
  readonly quotes = signal<Quote[]>([
    {
      id: 1, author: 'Marcus Aurelius',
      text: 'The impediment to action advances action. What stands in the way becomes the way.',
      createdAt: '2026-05-15T09:00:00Z', addedAt: '2026-05-30T10:00:00Z',
    },
    {
      id: 2, author: 'Seneca',
      text: 'We suffer more in imagination than in reality.',
      createdAt: '2026-05-15T09:01:00Z', addedAt: '2026-05-30T10:01:00Z',
    },
    {
      id: 3, author: 'Epictetus',
      text: 'It is not what happens to you, but how you react to it that matters.',
      createdAt: '2026-05-15T09:02:00Z', addedAt: '2026-05-30T10:02:00Z',
    },
    {
      id: 4, author: 'Marcus Aurelius',
      text: 'You have power over your mind, not outside events. Realise this, and you will find strength.',
      createdAt: '2026-05-15T09:03:00Z', addedAt: '2026-05-30T10:03:00Z',
    },
    {
      id: 5, author: 'Seneca',
      text: 'Luck is what happens when preparation meets opportunity.',
      createdAt: '2026-05-15T09:04:00Z', addedAt: '2026-05-30T10:04:00Z',
    },
    {
      id: 6, author: 'Epictetus',
      text: 'Make the best use of what is in your power and take the rest as it happens.',
      createdAt: '2026-05-15T09:05:00Z', addedAt: '2026-05-30T10:05:00Z',
    },
    {
      id: 7, author: 'Marcus Aurelius',
      text: 'Very little is needed to make a happy life; it is all within yourself.',
      createdAt: '2026-05-15T09:06:00Z', addedAt: '2026-05-30T10:06:00Z',
    },
  ]);

  addQuote(quote: Quote): void {
    // signal.update produces a new array — signals are immutable-by-convention
    this.quotes.update(list => [...list, quote]);
  }

  removeQuote(id: number): void {
    this.quotes.update(list => list.filter(q => q.id !== id));
  }
}
