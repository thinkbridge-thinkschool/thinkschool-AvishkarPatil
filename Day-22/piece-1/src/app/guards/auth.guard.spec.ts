// Pins the authGuard's two outcomes (so the "redirect when unauthenticated"
// claim is verified, not taken on faith): true when authenticated, a redirect
// UrlTree → /quotes?returnUrl=… when not.

import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed }                          from '@angular/core/testing';
import { provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { signal }                           from '@angular/core';
import { authGuard }                        from './auth.guard';
import { AuthService }                      from '../auth.service';

describe('authGuard', () => {
  const authed = signal(false);

  beforeEach(() => {
    authed.set(false);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),                                   // real Router → real createUrlTree
        { provide: AuthService, useValue: { isAuthenticated: () => authed() } },
      ],
    });
  });

  const run = (url = '/quotes/new') =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url } as RouterStateSnapshot));

  it('allows navigation when authenticated', () => {
    authed.set(true);
    expect(run()).toBe(true);
  });

  it('redirects to /quotes with returnUrl when unauthenticated', () => {
    const result = run('/quotes/new');
    expect(result).toBeInstanceOf(UrlTree);
    const url = (result as UrlTree).toString();
    expect(url).toContain('/quotes');
    expect(url).toContain('returnUrl');
  });
});
