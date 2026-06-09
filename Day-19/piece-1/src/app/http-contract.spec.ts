// ── Characterization test — pins the real Week-1 API contract + interceptors ─
//
// Written BEFORE any UI. It documents, against HttpTestingController:
//   1. the GET /api/quotes?page=N&size=N response SHAPE ({id, author, text, …})
//   2. the auth Bearer header the authInterceptor attaches
//   3. a real 4xx coming back as ProblemDetails → mapped to a typed AppError
//      whose .message is the friendly server detail
//   4. retry-with-backoff on idempotent GETs for transient (5xx) failures
//   5. NO retry on non-transient 4xx, and NO retry on non-idempotent POSTs
//
// These mirror the actual server (QuotesApi):
//   • QuoteRepository.GetAllAsync → Quote[] of {id, author, text, createdAt,
//     isDeleted, ownerId}  (System.Text.Json camelCase)
//   • ExceptionMiddleware → 400 ProblemDetails {title, detail, status}
//
// RETRY_CONFIG is overridden to 0 ms so backoff timers resolve on the next
// macrotask without wall-clock waits.

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed }                                     from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { errorInterceptor } from './interceptors/error.interceptor';
import { authInterceptor }  from './interceptors/auth.interceptor';
import { retryInterceptor, RETRY_CONFIG } from './interceptors/retry.interceptor';
import { AuthService } from './auth.service';
import { AppError }    from './models/app-error';
import { Quote }       from './models/quote';

const LIST_URL = '/api/quotes?page=1&size=10';

// Flush pending macrotasks (the retry backoff uses timer(0) with baseDelayMs=0).
const tick = () => new Promise<void>(resolve => setTimeout(resolve, 0));

describe('Week-1 API contract + HttpClient interceptors', () => {
  let http: HttpClient;
  let mock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // errorInterceptor (outer) → auth → retry (inner); timeout excluded so
        // tests don't wait on the 5 s real-time window.
        provideHttpClient(withInterceptors([errorInterceptor, authInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
        { provide: RETRY_CONFIG, useValue: { maxRetries: 2, baseDelayMs: 0 } },
      ],
    });
    http = TestBed.inject(HttpClient);
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => mock.verify());

  it('1. GET /api/quotes?page&size returns the documented Quote[] shape', async () => {
    let result: Quote[] | undefined;
    http.get<Quote[]>(LIST_URL).subscribe(r => (result = r));

    const req = mock.expectOne(LIST_URL);
    expect(req.request.method).toBe('GET');
    req.flush([
      {
        id: 1,
        author: 'Marcus Aurelius',
        text: 'The impediment to action advances action.',
        createdAt: '2026-01-01T00:00:00Z',
        isDeleted: false,
        ownerId: 1,
      },
    ]);
    await tick();

    expect(result).toBeDefined();
    const q = result![0];
    expect(typeof q.id).toBe('number');
    expect(typeof q.author).toBe('string');
    expect(typeof q.text).toBe('string');
    expect(q).toMatchObject({ id: 1, author: 'Marcus Aurelius' });
  });

  it('2. attaches the Bearer token to /api/quotes requests', () => {
    TestBed.inject(AuthService).token.set('test-token-123');

    http.get(LIST_URL).subscribe();
    const req = mock.expectOne(LIST_URL);

    expect(req.request.headers.get('Authorization')).toBe('Bearer test-token-123');
    req.flush([]);
  });

  it('3. maps a 400 ProblemDetails to a typed AppError carrying the server detail', async () => {
    let caught: unknown;
    http.post('/api/quotes', { author: '', text: 'x' }).subscribe({ error: e => (caught = e) });

    mock.expectOne('/api/quotes').flush(
      { title: 'Bad Request', detail: 'Author must be between 1 and 200 characters.', status: 400 },
      { status: 400, statusText: 'Bad Request' },
    );
    await tick();

    expect(caught).toBeInstanceOf(AppError);
    const err = caught as AppError;
    expect(err.status).toBe(400);
    expect(err.message).toBe('Author must be between 1 and 200 characters.');
  });

  it('4. retries an idempotent GET on a transient 503, then succeeds', async () => {
    let result: Quote[] | undefined;
    let caught: unknown;
    http.get<Quote[]>(LIST_URL).subscribe({ next: r => (result = r), error: e => (caught = e) });

    // attempt 1 → 503
    mock.expectOne(LIST_URL).flush('boom', { status: 503, statusText: 'Service Unavailable' });
    await tick();
    // attempt 2 → 503
    mock.expectOne(LIST_URL).flush('boom', { status: 503, statusText: 'Service Unavailable' });
    await tick();
    // attempt 3 → success
    mock.expectOne(LIST_URL).flush([
      { id: 9, author: 'Seneca', text: 'Luck is what happens when preparation meets opportunity.',
        createdAt: '2026-01-02T00:00:00Z', isDeleted: false, ownerId: null },
    ]);
    await tick();

    expect(caught).toBeUndefined();
    expect(result?.[0].author).toBe('Seneca');
  });

  it('5. does NOT retry a GET that returns a non-transient 400', async () => {
    let caught: unknown;
    http.get(LIST_URL).subscribe({ error: e => (caught = e) });

    mock.expectOne(LIST_URL).flush(
      { title: 'Bad Request', detail: 'bad page', status: 400 },
      { status: 400, statusText: 'Bad Request' },
    );
    await tick();

    mock.expectNone(LIST_URL); // no second attempt
    expect(caught).toBeInstanceOf(AppError);
    expect((caught as AppError).status).toBe(400);
  });

  it('6. does NOT retry a non-idempotent POST even on a transient 503', async () => {
    let caught: unknown;
    http.post('/api/quotes', {}).subscribe({ error: e => (caught = e) });

    mock.expectOne('/api/quotes').flush('boom', { status: 503, statusText: 'Service Unavailable' });
    await tick();

    mock.expectNone('/api/quotes'); // POST is not idempotent → not retried
    expect((caught as AppError).status).toBe(503);
  });
});
