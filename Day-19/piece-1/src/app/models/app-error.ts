// ── Typed application error, mapped from RFC 7807 ProblemDetails ───────────
//
// The real Week-1 API returns ProblemDetails on failure (System.Text.Json
// camelCase), e.g.:
//   400  DomainException  → { "title":"Bad Request",
//                             "detail":"Author must be between 1 and 200 characters.",
//                             "status":400 }   (QuotesApi/Middleware/ExceptionMiddleware.cs)
//   401  auth             → { "title":"Unauthorized",
//                             "detail":"Invalid email or password.", "status":401 }
//                            (Results.Problem(...) in AuthEndpointExtensions.cs)
//
// The API does NOT currently return ValidationProblemDetails (its minimal-API
// endpoints have no DataAnnotations validation filter), but the shape below
// tolerates one — if an `errors` map ever appears we surface it too, so the
// client doesn't silently ignore field errors when the contract grows.

import { HttpErrorResponse } from '@angular/common/http';

/** RFC 7807 ProblemDetails (+ optional ValidationProblemDetails `errors`). */
export interface ProblemDetails {
  type?:     string;
  title?:    string;
  status?:   number;
  detail?:   string;
  instance?: string;
  errors?:   Record<string, string[]>; // ValidationProblemDetails extension
}

/**
 * The single typed error every API consumer sees (the errorInterceptor maps
 * raw HttpErrorResponse → this before it reaches a component/service).
 * `message` is always safe to show to a user.
 */
export class AppError extends Error {
  constructor(
    readonly status: number,                       // HTTP status (0 = network/timeout)
    override readonly message: string,             // friendly, user-facing
    readonly detail?: string,                      // raw server detail (logs/debug)
    readonly fieldErrors?: Record<string, string[]>, // set only for ValidationProblemDetails
  ) {
    super(message);
    this.name = 'AppError';
  }
}

/** Best-effort extraction of a ProblemDetails body from an HttpErrorResponse. */
function readProblemDetails(err: HttpErrorResponse): ProblemDetails | null {
  const body = err.error;
  if (body && typeof body === 'object' && ('detail' in body || 'title' in body || 'errors' in body)) {
    return body as ProblemDetails;
  }
  return null;
}

/** Generic friendly fallback when the server gave no usable `detail`. */
function fallbackMessage(status: number): string {
  switch (status) {
    case 0:   return 'Could not reach the API. Is it running on :5075?';
    case 400: return 'The request was invalid.';
    case 401: return 'Please sign in to continue.';
    case 403: return 'You don’t have permission to do that.';
    case 404: return 'That item could not be found.';
    case 429: return 'Too many requests — please slow down.';
    default:  return status >= 500
      ? 'Something went wrong on the server. Please try again.'
      : `Request failed (HTTP ${status}).`;
  }
}

/**
 * Map a raw HttpErrorResponse to a typed AppError with a friendly message.
 * Preference order for the message: ValidationProblemDetails field errors →
 * ProblemDetails.detail → status-based fallback.
 */
export function toAppError(err: HttpErrorResponse): AppError {
  const pd = readProblemDetails(err);

  // ValidationProblemDetails: flatten the first field's first message.
  if (pd?.errors && Object.keys(pd.errors).length > 0) {
    const firstKey = Object.keys(pd.errors)[0];
    const firstMsg = pd.errors[firstKey]?.[0];
    return new AppError(
      err.status,
      firstMsg ?? pd.detail ?? fallbackMessage(err.status),
      pd.detail,
      pd.errors,
    );
  }

  // Plain ProblemDetails: the server detail is already human-readable
  // (e.g. the domain message), so prefer it.
  if (pd?.detail) {
    return new AppError(err.status, pd.detail, pd.detail);
  }

  return new AppError(err.status, fallbackMessage(err.status));
}
