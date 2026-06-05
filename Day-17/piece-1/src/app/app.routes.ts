// ── Routes — all feature views are LAZY (loadComponent) ────────────────────
//
// Against the real Week-1 API:
//   /quotes        → list   (GET /api/quotes?page=N&size=N → Quote[])
//   /quotes/new    → create (POST /api/quotes)            — GUARDED (authGuard)
//   /quotes/:id    → detail (GET /api/quotes/{id} → Quote) — :id is the Quote.id
//
// Every route uses loadComponent, so each view ships as its own chunk that the
// browser fetches on first navigation (verifiable in the Network tab / build
// output). Route ORDER matters: 'quotes/new' is declared before 'quotes/:id'
// so the literal segment wins and "new" is never parsed as an :id.

import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'quotes', pathMatch: 'full' },

  {
    path: 'quotes',
    title: 'Quotes',
    loadComponent: () =>
      import('./quotes-list/quotes-list.component').then(m => m.QuotesListComponent),
  },
  {
    path: 'quotes/new',
    title: 'New quote',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quote-form-signals/quote-form-signals.component').then(m => m.QuoteFormSignalsComponent),
  },
  {
    path: 'quotes/:id',
    title: 'Quote detail',
    loadComponent: () =>
      import('./quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent),
  },

  { path: '**', redirectTo: 'quotes' },
];
