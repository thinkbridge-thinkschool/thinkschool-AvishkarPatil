// Root standalone component — the routed app SHELL.
// Hosts the always-visible sign-in bar + a small nav, then a <router-outlet />
// into which the lazy feature routes render (/quotes, /quotes/new, /quotes/:id).
// The auth-bar stays in the shell so the sign-in UI is reachable on every route
// (the authGuard redirects unauthenticated users here for /quotes/new).
// Dark mode toggle lives in auth-bar so it's always co-located with auth state.

import { Component }                                  from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive }  from '@angular/router';
import { AuthBarComponent }                            from './auth-bar/auth-bar.component';
import { ToastComponent }                              from './toast.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AuthBarComponent, ToastComponent],
  template: `
    <div class="app-shell">
      <app-auth-bar />

      <nav class="nav" aria-label="Main navigation">
        <div class="nav-links">
          <a routerLink="/quotes" routerLinkActive="active"
             [routerLinkActiveOptions]="{ exact: true }">Quotes</a>
        </div>
      </nav>

      <router-outlet />
    </div>

    <app-toast />
  `,
  styles: [`
    /* ── Centered page container ────────────────────────────────── */
    .app-shell {
      max-width: 1200px;
      margin: 0 auto;
      padding: 2rem;
    }
    @media (max-width: 768px)  { .app-shell { padding: 1.5rem; } }
    @media (max-width: 480px)  { .app-shell { padding: 1rem 0.875rem; } }

    /* ── Nav bar ────────────────────────────────────────────────── */
    .nav { margin-bottom: 1.5rem; }

    /* ── Text nav links ─────────────────────────────────────────── */
    .nav-links { display: flex; gap: 0.75rem; align-items: center; }

    .nav-links a {
      color: var(--text-muted);
      text-decoration: none;
      font-size: 0.9rem;
      font-weight: 500;
      padding: 0.3rem 0.1rem;
      border-bottom: 2px solid transparent;
      transition: color 0.15s, border-color 0.15s;
    }
    .nav-links a:hover { color: var(--text-primary); }
    .nav-links a.active {
      color: var(--text-primary);
      font-weight: 700;
      border-bottom-color: var(--primary);
    }
    .nav-links a:focus-visible {
      outline: none;
      border-radius: 3px;
      box-shadow: 0 0 0 3px var(--primary-ring);
    }
  `],
})
export class AppComponent {}
