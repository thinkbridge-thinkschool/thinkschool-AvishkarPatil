// ── ViewState<T> — Day-16 piece-2 state-management primitive ───────────────
//
// A discriminated union that models the FOUR states every async read screen
// actually has: loading, error, empty, loaded. Instead of a component juggling
// three separate signals (isLoading / error / value) and reconstructing the
// state with nested @if/@else, the store derives ONE computed<ViewState<T>>
// and the template switches on `.status` with @switch.
//
// This is the "model the feature's state with signals" idea made concrete:
// the legal states are a closed set in the type system, so the template can't
// forget a branch and the store can't emit an impossible combination (e.g.
// loading AND error at once).
//
// `empty` is distinct from `loaded` on purpose — "the request succeeded but
// returned zero rows" is a different screen from "here are your rows", and the
// Week-1 list endpoint (GET /api/quotes) legitimately returns [] on a page
// past the end.

export type ViewState<T> =
  | { status: 'loading' }
  | { status: 'error';  message: string }
  | { status: 'empty' }
  | { status: 'loaded'; data: T };
