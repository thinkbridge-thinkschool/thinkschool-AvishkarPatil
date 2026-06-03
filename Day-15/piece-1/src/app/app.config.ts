import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors }               from '@angular/common/http';
import { errorInterceptor }                                  from './interceptors/error.interceptor';
import { authInterceptor }                                   from './interceptors/auth.interceptor';
import { retryInterceptor }                                  from './interceptors/retry.interceptor';
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
// Interceptor order matters — request flows top→bottom, the error/response
// flows bottom→top:
//   errorInterceptor   (outermost) maps the FINAL error → typed AppError
//   authInterceptor    attaches the Bearer token — runs once, but the cloned
//                      request it returns is what retry replays, so the header
//                      is present on every retried attempt
//   retryInterceptor   retries idempotent GETs on transient failures
//   timeoutInterceptor (innermost) fails one attempt fast if the API hangs
// So retry sees the RAW HttpErrorResponse (to decide retryability) and only the
// final, un-retryable error reaches errorInterceptor to become an AppError.
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors([
      errorInterceptor,
      authInterceptor,
      retryInterceptor,
      timeoutInterceptor,
    ])),
  ],
};
