// ── errorInterceptor — map ProblemDetails → typed AppError ─────────────────
//
// Outermost interceptor: it catches the FINAL HttpErrorResponse (after the
// retry interceptor has given up) and converts it to a typed AppError carrying
// a friendly, user-facing message derived from the server's ProblemDetails.
// Every downstream consumer (services, components, httpResource.error()) then
// deals with ONE error type instead of poking at raw status codes.
//
// Non-HTTP errors (e.g. the rxjs TimeoutError from the timeoutInterceptor) are
// normalised to an AppError with status 0 so the UI still gets a friendly line.

import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError }               from 'rxjs';
import { AppError, toAppError }                 from '../models/app-error';

export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        return throwError(() => toAppError(err));
      }
      if (err instanceof AppError) {
        return throwError(() => err); // already mapped
      }
      // TimeoutError or anything else → treat as unreachable (status 0).
      return throwError(() => new AppError(0, 'Could not reach the API. Is it running on :5075?'));
    }),
  );
