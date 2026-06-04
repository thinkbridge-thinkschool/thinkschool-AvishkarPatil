// ── retryInterceptor — retry IDEMPOTENT GETs with exponential backoff ──────
//
// Retries only:
//   • GET requests (idempotent — safe to repeat; a POST/PUT/DELETE could
//     double-create or double-delete, so they are never retried), and
//   • TRANSIENT failures: network error (status 0), 429, or 5xx.
//
// 4xx (except 429) are deterministic client errors — retrying them just wastes
// time and still fails, so we surface them immediately. Timeouts from the
// timeoutInterceptor arrive as a non-HttpErrorResponse error and are also not
// retried (the API is down — don't hang for another 2×5 s).
//
// Backoff is exponential: baseDelay, baseDelay×2, … (300 ms, 600 ms by default).
// maxRetries/baseDelay are injectable so tests can set the delay to 0.

import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, inject }               from '@angular/core';
import { retry, timer }                         from 'rxjs';

export interface RetryConfig {
  maxRetries:  number;
  baseDelayMs: number;
}

export const RETRY_CONFIG = new InjectionToken<RetryConfig>('RETRY_CONFIG', {
  factory: () => ({ maxRetries: 2, baseDelayMs: 300 }),
});

function isTransient(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false; // e.g. TimeoutError
  return error.status === 0 || error.status === 429 || error.status >= 500;
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  const { maxRetries, baseDelayMs } = inject(RETRY_CONFIG);

  return next(req).pipe(
    retry({
      count: maxRetries,
      delay: (error, retryCount) => {
        if (!isTransient(error)) {
          throw error; // non-transient → stop retrying, surface now
        }
        // retryCount is 1-based: 300 ms, then 600 ms, …
        return timer(baseDelayMs * 2 ** (retryCount - 1));
      },
    }),
  );
};
