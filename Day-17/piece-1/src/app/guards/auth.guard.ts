// ── authGuard — functional CanActivateFn ───────────────────────────────────
//
// Guards routes that require a signed-in writer (e.g. /quotes/new, which POSTs
// to the can-edit-quotes endpoint). Reading quotes is public, so only the
// create route is protected — guarding the public detail route would be theatre.
//
// Returns:
//   • true                       when authenticated → navigation proceeds
//   • a redirect UrlTree → /quotes when not, preserving the intended URL as
//     ?returnUrl so the app could bounce the user back after they sign in
//     (the sign-in bar lives on the list route).

import { inject }                  from '@angular/core';
import { CanActivateFn, Router }   from '@angular/router';
import { AuthService }             from '../auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/quotes'], {
    queryParams: { returnUrl: state.url },
  });
};
