import { HttpInterceptorFn } from '@angular/common/http';
import { timeout }           from 'rxjs';

// How long to wait before giving up on a request.
// Day-17: the API now runs on Azure Container Apps with min-replicas=0
// (scale-to-zero — the free-tier trade-off). The FIRST request after the app
// has been idle pays a cold-start penalty (.NET boot + EnsureCreated + seed),
// which can exceed several seconds. 30 s tolerates that cold start; a warm app
// still answers in well under 500 ms. Raise/lower with min-replicas.
export const REQUEST_TIMEOUT_MS = 30000;

// Functional HTTP interceptor that fails a request fast instead of letting it
// hang forever.
//
// WHY this exists: when the Week-1 API is stopped, the Angular dev-server
// proxy cannot reach :5075 and leaves the XHR pending — it never returns a
// status code the browser can observe.  Without a timeout, httpResource stays
// in its loading state indefinitely and the UI never reaches the error branch.
//
// rxjs `timeout(ms)` throws a TimeoutError once the window elapses.  That error
// propagates back through HttpClient into the httpResource, which then sets
// isLoading() → false and error() → the TimeoutError, so the component's
// `@else if (error())` branch finally renders.
export const timeoutInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(timeout(REQUEST_TIMEOUT_MS));
