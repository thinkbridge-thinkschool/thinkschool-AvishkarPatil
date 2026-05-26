# Why this pipeline order, and not another?

The pipeline ([Extensions/HttpResilienceExtensions.cs](Extensions/HttpResilienceExtensions.cs)) is built in this order:

```
1. total timeout (10s)        ← outermost
2. retry (3, exp + jitter)
3. circuit breaker (50%/30s)
4. per-attempt timeout (3s)   ← innermost
```

Polly v8 composes inside-out: the strategy added *first* is the *outermost* wrapper, the strategy added *last* runs *first* on each attempt. So the actual control flow for one call is:

```
client.SendAsync()
  └─ outer total-timeout starts a 10s budget for the whole thing
       └─ retry strategy starts attempt #1
            └─ circuit-breaker checks: is the circuit open? if so, throw and skip the attempt
                 └─ per-attempt timeout starts a 3s budget
                      └─ ACTUAL HTTP call
                 └─ per-attempt timeout ends
            └─ circuit-breaker records the outcome (success / handled failure)
       └─ retry decides: handled failure? sleep with jitter, then attempt #2…
  └─ outer total-timeout ends (or trips at 10s and cancels everything)
```

Every strategy has a job and the job only makes sense at its position in the stack. Move one and the system gets worse in a specific, predictable way.

---

## Why per-attempt timeout is *innermost*

A single network call must be bounded in isolation — otherwise one slow attempt eats the whole retry budget. If the dependency is just slow (rather than failing), without a per-attempt timeout the first attempt blocks for the entire `HttpClient.Timeout` (default 100s), and we never get to attempt #2. The 3s ceiling forces a fast fail so retry can do its job.

A separate per-attempt timeout — not `HttpClient.Timeout` — is also what lets Polly cancel via its own `CancellationToken` and tag the outcome as `TimeoutRejectedException` (which is in the retry strategy's default `ShouldHandle` predicate). `HttpClient.Timeout` raises `OperationCanceledException`, which behaves differently.

## Why circuit breaker sits *above* per-attempt timeout, but *below* retry

The breaker has to see attempt outcomes — both successful responses and failures-after-timeout — to keep its rolling failure-rate window accurate. That puts it above the per-attempt timeout.

But the breaker has to be *inside* the retry, not outside it. Two reasons:

1. **Single-call breaker math.** If retry is inside the breaker, a single user request that retries 3 times and fails registers as 4 separate failed samples. The breaker would open after one bad caller instead of after a sustained period of dependency badness.
2. **Auto-recovery.** When the breaker is inside retry, an opened circuit throws `BrokenCircuitException` from `Execute*`. The outer retry strategy treats that as a retryable outcome (it's in the default `ShouldHandle`), waits the backoff, and tries again. After the break duration the circuit goes half-open, the next call probes the dependency, and if it succeeds the circuit closes — all without any caller-visible difference except the user sees one transparent recovery instead of a hard failure.

## Why retry sits *above* breaker, but *below* total timeout

Retry needs a hard ceiling, otherwise 3 retries × jittered exponential backoff × (3s per attempt + breaker delays) can stack into 20–30 seconds during a real outage, which is worse than just returning an error to the user.

The 10s total timeout caps that: even if every strategy below it goes pathological, the caller sees a definitive timeout at 10s. That keeps the SLO of *this* service decoupled from the worst-case behavior of the *dependency*.

The total timeout has to be the outermost wrapper, because that's the only way it can cancel work that's currently sitting inside backoff sleeps or inside the per-attempt timeout. If you put it lower in the stack, it only times out the inner work, not the time spent sleeping between retries.

---

## A consequence of this ordering: idempotency

This order is correct for `GET`s. It is risky for `POST`s. A retry strategy that fires after a transient 5xx has no way to know whether the server already processed the request and crashed *after* committing the side effect. For the OIDC metadata fetch this doesn't matter (the call is a `GET` and the response is idempotent), but anything that mutates state needs either an idempotency key on the request or a tighter `ShouldHandle` predicate that retries only on errors which *prove* no side effect happened (connect-refused, DNS NXDOMAIN, pre-headers `HttpRequestException`).

That's the main thing this piece *doesn't* solve — see Q2 in the [README](README.md).
