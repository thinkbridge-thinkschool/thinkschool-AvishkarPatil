import { HttpInterceptorFn } from '@angular/common/http';
import { inject }            from '@angular/core';
import { AuthService }       from '../auth.service';

// Attaches the writer's bearer token to API calls so POST /api/quotes can pass
// the can-edit-quotes policy. The login call itself is unauthenticated, so it's
// skipped (attaching a token there is harmless but pointless).
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();

  if (token && req.url.startsWith('/api') && !req.url.startsWith('/api/auth/')) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(req);
};
