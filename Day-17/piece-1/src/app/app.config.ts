import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions, withPreloading, PreloadAllModules } from '@angular/router';
import { provideHttpClient, withInterceptors }               from '@angular/common/http';
import { routes }                                            from './app.routes';
import { errorInterceptor }                                  from './interceptors/error.interceptor';
import { authInterceptor }                                   from './interceptors/auth.interceptor';
import { retryInterceptor }                                  from './interceptors/retry.interceptor';
import { timeoutInterceptor }                                from './interceptors/timeout.interceptor';
import { apiBaseInterceptor, API_BASE_URL }                  from './interceptors/api-base.interceptor';

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
// provideRouter:
//   routes                      — all feature views lazy-loaded (loadComponent)
//   withComponentInputBinding() — binds the :id route param to QuoteDetail's
//                                 `id` input (no manual ActivatedRoute plumbing)
//   withViewTransitions()       — wraps each navigation in document.startView-
//                                 Transition, so list ↔ detail cross-fades (and
//                                 the detail card's view-transition-name morphs)
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions(), withPreloading(PreloadAllModules)),
    // Day-17 Managed-Identity path: the SPA calls the BROKER Container App (which
    // holds a system-assigned MI), NOT the API directly. The browser holds no
    // token; the broker acquires the MI token and forwards to the Week-1 API.
    // apiBaseInterceptor rewrites relative /api/... URLs to this broker origin.
    { provide: API_BASE_URL, useValue: 'https://ca-quotes-broker.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io' },
    provideHttpClient(withInterceptors([
      errorInterceptor,
      authInterceptor,       // matches relative /api to attach the Bearer token…
      apiBaseInterceptor,    // …then rewrites /api → the absolute Container App URL
      retryInterceptor,
      timeoutInterceptor,
    ])),
  ],
};
