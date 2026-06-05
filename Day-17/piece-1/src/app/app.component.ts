// Root standalone component — the routed app SHELL.
// Hosts the always-visible sign-in bar + a small nav, then a <router-outlet />
// into which the lazy feature routes render (/quotes, /quotes/new, /quotes/:id).
// The auth-bar stays in the shell so the sign-in UI is reachable on every route
// (the authGuard redirects unauthenticated users here for /quotes/new).

import { Component }                       from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthBarComponent }                from './auth-bar/auth-bar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AuthBarComponent],
  template: `
    <app-auth-bar />

    <nav class="nav">
      <a routerLink="/quotes" routerLinkActive="active"
         [routerLinkActiveOptions]="{ exact: true }">Quotes</a>
      <a routerLink="/quotes/new" routerLinkActive="active">＋ New quote</a>
    </nav>

    <main>
      <router-outlet />
    </main>
  `,
  styles: [`
    .nav { display: flex; gap: 1rem; margin-bottom: 1.5rem; }
    .nav a { color: #0a58ca; text-decoration: none; padding: 0.25rem 0; }
    .nav a:hover { text-decoration: underline; }
    .nav a.active { font-weight: 700; border-bottom: 2px solid #0d6efd; }
  `],
})
export class AppComponent {}
