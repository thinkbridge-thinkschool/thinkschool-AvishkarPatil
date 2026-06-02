import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors }               from '@angular/common/http';
import { authInterceptor }                                   from './interceptors/auth.interceptor';
import { timeoutInterceptor }                                from './interceptors/timeout.interceptor';

// provideZonelessChangeDetection() replaces zone.js entirely.
// Angular no longer needs to be notified via zone patches; instead, every
// signal write marks the consuming view as dirty and schedules a microtask
// to flush pending updates. This makes rendering fully reactive and removes
// the ~100 KB zone.js payload from the production bundle.
//
// provideHttpClient() registers HttpClient so the signals-first httpResource()
// in QuotesService can call the real Week-1 API (GET /api/collections/{id}).
// Requests go to /api/... and are proxied to http://localhost:5075 by the
// Angular dev server (see proxy.conf.json) — same-origin, no CORS needed.
//
// withInterceptors([timeoutInterceptor]) makes every request fail fast if the
// backend is unreachable.  Without it, a stopped API leaves the request
// pending and httpResource never leaves its loading state — the error branch
// would never render.  The timeout converts the hang into an error the UI can
// show.
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors([authInterceptor, timeoutInterceptor])),
  ],
};
