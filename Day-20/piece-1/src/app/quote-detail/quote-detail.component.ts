// ── QuoteDetailComponent — lazy route /quotes/:id ──────────────────────────
//
// Driven by the same signals-first ViewState pattern as QuotesList.
// Reads the :id route param via withComponentInputBinding() → `id` input,
// validates it, then calls QuotesService.selectQuote() to trigger the
// detail fetch.  The template branches on detailView().status.
//
// States: invalid-id / loading / error (404 or API down) / loaded.
// The hero card carries view-transition-name so list→detail animates.
//
// New in this revision: hero card layout, human-readable dates,
// collapsible technical section, Copy and Share actions.
// No API changes, no new dependencies, no routing changes.
// ─────────────────────────────────────────────────────────────────────────

import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink }    from '@angular/router';
import { QuotesService } from '../quotes.service';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [RouterLink],
  template: `
    <!-- ── Breadcrumb / back navigation ──────────────────────── -->
    <nav class="breadcrumb" aria-label="Breadcrumb">
      <a routerLink="/quotes" aria-label="Back to Quotes list">‹ Quotes</a>
      <span class="bc-sep" aria-hidden="true">›</span>
      <span aria-current="page">Quote Details</span>
    </nav>

    <!-- ── Page header ───────────────────────────────────────── -->
    <header class="page-header">
      <div>
        <h1>Quote Details</h1>
        <p class="page-subtitle">View complete information about this quote</p>
      </div>
      <a class="btn-back" routerLink="/quotes" aria-label="Back to Quotes list">
        <span aria-hidden="true">←</span> Back to Quotes
      </a>
    </header>

    <!-- ── Body ──────────────────────────────────────────────── -->
    @if (invalidId()) {
      <div class="status-card status-card--error" role="alert">
        <span class="status-icon" aria-hidden="true">⚠️</span>
        <p>"{{ id() }}" is not a valid quote id.</p>
      </div>
    } @else {
      @switch (view().status) {

        @case ('error') {
          <div class="status-card status-card--error" role="alert">
            <span class="status-icon" aria-hidden="true">⚠️</span>
            <p>{{ errorMessage() }}</p>
          </div>
        }

        @case ('loaded') {
          <div class="detail-layout">

            <!-- ── Hero quote card ──────────────────────────── -->
            <article class="quote-hero">
              <span class="deco-quote" aria-hidden="true">&ldquo;</span>
              <blockquote class="hero-text">{{ quote()!.text }}</blockquote>
              <div class="hero-author">
                <span class="author-avatar"
                      [style.background-color]="avatarColor(quote()!.author)"
                      aria-hidden="true">{{ initials(quote()!.author) }}</span>
                <span class="author-name">{{ quote()!.author }}</span>
              </div>
            </article>

            <!-- ── Action bar ───────────────────────────────── -->
            <div class="action-bar" role="toolbar" aria-label="Quote actions">
              <button class="btn-action"
                      [class.btn-action--done]="copied()"
                      type="button"
                      (click)="copyQuote()"
                      aria-label="Copy quote text to clipboard">
                {{ copied() ? '✓ Copied!' : '📋 Copy Quote' }}
              </button>
              <button class="btn-action btn-action--ghost"
                      type="button"
                      (click)="shareQuote()"
                      aria-label="Share this quote">
                🔗 Share
              </button>
            </div>

            <!-- ── Metadata card ─────────────────────────────── -->
            <section class="meta-card" aria-label="Quote information">
              <div class="meta-row">
                <span class="meta-label">Created</span>
                <span class="meta-value">{{ formatDate(quote()!.createdAt) }}</span>
              </div>

              <!-- Collapsible technical details for power users -->
              <div class="tech-section">
                <button class="tech-toggle"
                        type="button"
                        (click)="techExpanded.update(v => !v)"
                        [attr.aria-expanded]="techExpanded()">
                  <span class="tech-chevron"
                        [class.tech-chevron--open]="techExpanded()"
                        aria-hidden="true">›</span>
                  Technical Details
                </button>
                @if (techExpanded()) {
                  <div class="tech-body">
                    <div class="meta-row">
                      <span class="meta-label">Quote ID</span>
                      <span class="meta-value meta-value--mono">{{ quote()!.id }}</span>
                    </div>
                    <div class="meta-row meta-row--last">
                      <span class="meta-label">Owner ID</span>
                      <span class="meta-value meta-value--mono">
                        {{ quote()!.ownerId ?? '—' }}
                      </span>
                    </div>
                  </div>
                }
              </div>
            </section>

          </div>
        }

        @default {
          <!-- loading + empty states -->
          <div class="loading-state" aria-busy="true" aria-label="Loading quote">
            <div class="skeleton-hero">
              <div class="skel skel-deco"></div>
              <div class="skel skel-line skel-line--lg"></div>
              <div class="skel skel-line skel-line--md"></div>
              <div class="skel skel-line skel-line--sm"></div>
              <div class="skel skel-author"></div>
            </div>
          </div>
        }

      }
    }
  `,
  styles: [`
    /* ── Breadcrumb ─────────────────────────────────────────── */
    .breadcrumb {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      font-size: 0.8rem;
      color: var(--text-muted);
      margin-bottom: 1.25rem;
    }
    .breadcrumb a {
      color: var(--primary);
      text-decoration: none;
      font-weight: 500;
      transition: color 0.15s;
    }
    .breadcrumb a:hover { color: var(--primary-dark); text-decoration: underline; }
    .breadcrumb a:focus-visible {
      outline: none;
      border-radius: 3px;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .bc-sep { color: var(--border-muted); }

    /* ── Page header ────────────────────────────────────────── */
    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 1rem;
      max-width: 860px;
      margin-bottom: 1.75rem;
      flex-wrap: wrap;
    }
    .page-header h1 {
      margin: 0 0 0.2rem;
      font-size: 1.65rem;
      font-weight: 700;
      color: var(--text-primary);
    }
    .page-subtitle {
      font-size: 0.875rem;
      color: var(--text-muted);
      margin: 0;
    }

    /* Back button — secondary / ghost style */
    .btn-back {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      font: inherit;
      font-size: 0.85rem;
      font-weight: 500;
      padding: 0.4rem 0.9rem;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface);
      color: var(--text-secondary);
      text-decoration: none;
      white-space: nowrap;
      flex-shrink: 0;
      transition: background 0.15s, border-color 0.15s, transform 0.1s;
    }
    .btn-back:hover {
      background: var(--surface-hover);
      border-color: var(--border-muted);
      transform: translateY(-1px);
    }
    .btn-back:active { transform: translateY(0); }
    .btn-back:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }

    /* ── Detail layout ──────────────────────────────────────── */
    .detail-layout {
      max-width: 860px;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }

    /* ── Hero quote card ────────────────────────────────────── */
    .quote-hero {
      background: var(--surface);
      border: 1px solid var(--border);
      border-left: 4px solid var(--primary);
      border-radius: 12px;
      padding: 2rem 2rem 1.75rem;
      box-shadow: var(--shadow-sm);
      /* Animate across list→detail navigation. */
      view-transition-name: quote-detail-card;
    }

    .deco-quote {
      display: block;
      font-size: 5rem;
      line-height: 0.9;
      color: var(--primary-tint);
      font-family: Georgia, 'Times New Roman', serif;
      margin-bottom: -1rem;
      user-select: none;
    }

    .hero-text {
      font-size: 1.3rem;
      line-height: 1.7;
      color: var(--text-primary);
      font-style: italic;
      margin: 0 0 1.5rem;
      quotes: none;
    }

    .hero-author {
      display: flex;
      align-items: center;
      gap: 0.65rem;
    }
    .author-avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: 50%;
      font-size: 0.7rem;
      font-weight: 700;
      color: #fff;
      flex-shrink: 0;
      letter-spacing: 0.02em;
      text-transform: uppercase;
    }
    .author-name {
      font-size: 1rem;
      font-weight: 600;
      color: var(--text-primary);
    }

    /* ── Action bar ─────────────────────────────────────────── */
    .action-bar {
      display: flex;
      gap: 0.6rem;
      flex-wrap: wrap;
    }
    .btn-action {
      display: inline-flex;
      align-items: center;
      gap: 0.3rem;
      font: inherit;
      font-size: 0.875rem;
      font-weight: 600;
      padding: 0.45rem 1.1rem;
      border: 0;
      border-radius: 6px;
      background: var(--primary);
      color: #fff;
      cursor: pointer;
      transition: background 0.15s, transform 0.1s, box-shadow 0.15s;
    }
    .btn-action:hover {
      background: var(--primary-dark);
      transform: translateY(-1px);
    }
    .btn-action:active { transform: translateY(0); }
    .btn-action:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .btn-action--done {
      background: var(--success);
    }
    .btn-action--done:hover { background: var(--success-dark); }
    .btn-action--ghost {
      background: var(--surface);
      color: var(--primary);
      border: 1px solid var(--border);
    }
    .btn-action--ghost:hover {
      background: var(--primary-tint);
      border-color: var(--border-hover);
    }

    /* ── Metadata card ──────────────────────────────────────── */
    .meta-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 1.25rem 1.5rem;
      box-shadow: var(--shadow-sm);
    }
    .meta-row {
      display: flex;
      align-items: baseline;
      gap: 0.75rem;
      padding: 0.5rem 0;
      border-bottom: 1px solid var(--border);
    }
    .meta-row--last,
    .meta-row:last-child { border-bottom: 0; padding-bottom: 0; }
    .meta-label {
      font-size: 0.75rem;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
      min-width: 6.5rem;
      flex-shrink: 0;
    }
    .meta-value {
      font-size: 0.875rem;
      color: var(--text-primary);
    }
    .meta-value--mono {
      font-family: ui-monospace, 'Cascadia Mono', 'Segoe UI Mono', monospace;
      font-size: 0.82rem;
      background: var(--code-bg);
      padding: 0.1rem 0.35rem;
      border-radius: 4px;
    }

    /* Collapsible technical details */
    .tech-section { margin-top: 0.25rem; }
    .tech-toggle {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      font: inherit;
      font-size: 0.8rem;
      font-weight: 500;
      color: var(--text-muted);
      background: none;
      border: 0;
      cursor: pointer;
      padding: 0.3rem 0;
      transition: color 0.15s;
    }
    .tech-toggle:hover { color: var(--primary); }
    .tech-toggle:focus-visible {
      outline: none;
      border-radius: 3px;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
    .tech-chevron {
      font-size: 0.95rem;
      display: inline-block;
      transition: transform 0.2s;
    }
    .tech-chevron--open { transform: rotate(90deg); }
    .tech-body { padding-top: 0.25rem; }

    /* ── Status / error card ────────────────────────────────── */
    .status-card {
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 1.25rem 1.5rem;
      max-width: 640px;
      font-size: 0.9rem;
      color: var(--text-secondary);
    }
    .status-card p { margin: 0; }
    .status-card--error {
      border-color: var(--danger);
      color: var(--danger);
    }
    .status-icon { font-size: 1.2rem; flex-shrink: 0; }

    /* ── Loading skeleton ───────────────────────────────────── */
    .loading-state { max-width: 860px; }
    .skeleton-hero {
      background: var(--surface);
      border: 1px solid var(--border);
      border-left: 4px solid var(--border);
      border-radius: 12px;
      padding: 2rem;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .skel {
      background: linear-gradient(
        90deg,
        var(--surface-subtle) 25%,
        var(--border) 50%,
        var(--surface-subtle) 75%
      );
      background-size: 200% 100%;
      animation: shimmer 1.4s infinite;
      border-radius: 4px;
    }
    @keyframes shimmer {
      0%   { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }
    .skel-deco      { height: 3rem; width: 2.5rem; }
    .skel-line      { height: 0.9rem; }
    .skel-line--lg  { height: 1.4rem; width: 95%; }
    .skel-line--md  { width: 88%; }
    .skel-line--sm  { width: 55%; }
    .skel-author    { height: 2.5rem; width: 11rem; border-radius: 2rem; margin-top: 0.5rem; }

    /* ── Responsive ─────────────────────────────────────────── */
    @media (max-width: 640px) {
      .page-header { flex-direction: column-reverse; gap: 0.75rem; }
      .page-header h1 { font-size: 1.35rem; }
      .quote-hero { padding: 1.5rem 1.25rem 1.25rem; }
      .hero-text { font-size: 1.1rem; }
      .deco-quote { font-size: 3.5rem; }
      .meta-card { padding: 1rem 1.125rem; }
      .meta-label { min-width: 5.5rem; }
    }
    @media (max-width: 420px) {
      .btn-action { font-size: 0.8rem; padding: 0.4rem 0.875rem; }
      .btn-back { font-size: 0.8rem; padding: 0.35rem 0.75rem; }
    }
  `],
})
export class QuoteDetailComponent {
  private readonly quotes = inject(QuotesService);

  // Route param bound via withComponentInputBinding().
  readonly id = input.required<string>();

  protected readonly parsedId = computed<number | null>(() => {
    const n = Number(this.id());
    return Number.isInteger(n) && n > 0 ? n : null;
  });
  protected readonly invalidId = computed<boolean>(() => this.parsedId() === null);

  protected readonly view  = this.quotes.detailView;
  protected readonly quote = this.quotes.selectedQuote;

  protected readonly errorMessage = computed<string>(() => {
    const v = this.view();
    return v.status === 'error' ? v.message : 'Could not load this quote.';
  });

  // ── UI state ──────────────────────────────────────────────────────
  protected readonly copied       = signal<boolean>(false);
  protected readonly techExpanded = signal<boolean>(false);

  constructor() {
    effect(() => {
      const n = this.parsedId();
      if (n !== null) this.quotes.selectQuote(n);
    });
  }

  // ── Actions ───────────────────────────────────────────────────────
  protected async copyQuote(): Promise<void> {
    const q = this.quote();
    if (!q) return;
    try {
      await navigator.clipboard.writeText(q.text);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard unavailable — silent fail; user can still select text
    }
  }

  protected shareQuote(): void {
    const q = this.quote();
    if (!q) return;
    const text = `"${q.text}" — ${q.author}`;
    const url  = window.location.href;
    const nav  = navigator as Navigator & { share?: (d: ShareData) => Promise<void> };
    if (nav.share) {
      nav.share({ title: `Quote by ${q.author}`, text, url }).catch(() => {});
    } else {
      navigator.clipboard.writeText(`${text}\n${url}`).catch(() => {});
    }
  }

  // ── Display helpers ───────────────────────────────────────────────
  protected formatDate(iso: string): string {
    if (!iso) return '—';
    try {
      return new Date(iso).toLocaleString(undefined, {
        weekday: 'short',
        year: 'numeric',
        month: 'long',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
      });
    } catch {
      return iso;
    }
  }

  private readonly _palette = [
    '#0d6efd', '#198754', '#6f42c1', '#d63384',
    '#0dcaf0', '#fd7e14', '#20c997', '#6610f2',
  ];

  protected initials(author: string): string {
    return author.trim().split(/\s+/).slice(0, 2)
      .map(w => w[0]?.toUpperCase() ?? '').join('');
  }

  protected avatarColor(author: string): string {
    let h = 0;
    for (let i = 0; i < author.length; i++) {
      h = (Math.imul(31, h) + author.charCodeAt(i)) | 0;
    }
    return this._palette[Math.abs(h) % this._palette.length];
  }
}
