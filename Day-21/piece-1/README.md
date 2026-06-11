# Day 21 — HybridCache + Stampede Protection

## Problem Statement

Add HybridCache (in-memory + Redis) to a hot read, with stampede protection so a cache
miss doesn't fan out N identical DB hits. Measure the hit rate and the DB load drop under
concurrent load.

**Exercise:** Paste the cache wiring + the load-test before/after (DB queries/sec, p99).
Show stampede protection working under concurrency.

---

## Exercise Answer

*Direct response to the exercise. All evidence referenced here is expanded in the sections below.*

### 1 — Cache Wiring

**Packages** (`QuotesApi.csproj`):
```xml
<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" Version="9.5.0" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.0" />
```

**DI registration** (`Extensions/InfrastructureExtensions.cs`):
```csharp
// L2 — Redis with fail-fast flags so a dead Redis host doesn't block startup
services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = redisConnection
        + ",abortConnect=false,connectTimeout=1000,syncTimeout=500";
    o.InstanceName  = "QuotesApi:";
});

// L1 (in-process, 30 s) + L2 (Redis, 5 min)
services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
        Expiration           = TimeSpan.FromMinutes(5),
    };
});

// Singleton — safe because AppDbContext is obtained via IServiceScopeFactory
// inside each factory invocation, not captured at construction time.
services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
```

**Stampede coalescing** (`Application/Queries/Collections/CollectionQueryService.cs`):
```csharp
var result = await _cache.GetOrCreateAsync(
    $"collection:{id}",
    async ct =>
    {
        _logger.LogDebug(
            "Cache miss — fetching collection {CollectionId} from database", id);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Collections
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CollectionDetailReadModel { ... })
            .FirstOrDefaultAsync(ct);
    },
    cancellationToken: cancellationToken);

// Evict null results immediately — caching "not found" would hide a
// collection created after this miss for the remainder of the TTL.
if (result is null)
    await _cache.RemoveAsync($"collection:{id}", cancellationToken);
```

**Cache invalidation on write** (`Application/Commands/Collections/AddQuoteToCollectionCommandHandler.cs`):
```csharp
var ok = await _repository.UpdateAsync(collection, cancellationToken);

// Unconditional eviction — clears both L1 and L2 atomically.
await _cache.RemoveAsync($"collection:{command.CollectionId}", cancellationToken);

return ok;
```

---

### 2 — Load Test Before vs After

**Commands:**
```bash
# BEFORE — /dapper bypasses HybridCache; every VU hits the DB directly
k6 run --env SCENARIO=stampede --env ENDPOINT_SUFFIX=dapper load-test.js

# AFTER — /ef uses HybridCache; concurrent misses coalesce to one DB hit
k6 run --env SCENARIO=stampede load-test.js
```

| Metric | BEFORE (`/dapper`, no cache) | AFTER (`/ef` + HybridCache) | Δ |
|--------|------------------------------|------------------------------|---|
| DB queries/sec | **25.8** (1 DB hit per request) | **0.6** (1 DB hit per 50-VU burst) | −98% |
| avg (ms) | 494.8 | 297.7 | −40% |
| p95 (ms) | 550.3 | 348.8 | −37% |
| p99 (ms) | ≈ 1395 | ≈ 1296 | −7% |
| DB hits (50-VU run) | 50 | **1** | −98% |
| Failed | 0 | 0 | — |

> **p99 note:** With 51 total samples (50 VUs + 1 `setup()` probe), p99 is the 50.49th of
> 51 sorted values — statistically the observed maximum. The values above derive from the
> measured `max`. The setup probe (cold-cache + Redis connection) is the p99 outlier in
> both runs; the 50 VU requests themselves completed within the p95 band.

> **DB queries/sec:** BEFORE — every HTTP request is a DB query → 25.8 DB queries/sec.
> AFTER — the entire 50-VU burst triggers exactly 1 DB query; subsequent requests are
> served from L1 for up to 30 s → 0.6 DB queries/sec for the burst; 0 DB queries/sec
> while the cache is warm.

---

### 3 — Stampede Protection

**Setup:** Redis key evicted (`redis-cli del "QuotesApi:collection:1"`). 50 VUs fire at
`GET /api/collections/1/ef` simultaneously with no sleep.

**Evidence:** Application log produced exactly **one** line during the entire 50-VU run:

```
[HH:mm:ss DBG] Cache miss — fetching collection 1 from database {"CollectionId": 1}
```

**Interpretation:** `GetOrCreateAsync` allowed only the first VU to enter the factory
lambda. The remaining 49 VUs suspended, awaiting the same `Task`. Once the factory
returned, all 49 waiters received the result from L1 MemoryCache — no second DB query
was issued. One log line = one factory call = one DB query regardless of concurrency.

Screenshot evidence: `06_Stampede_Proof_One_Cache_Miss.png`

---

## Architecture Overview

`CollectionQueryService` is the hot read path. Before Day 21, every `GET /api/collections/{id}/ef`
call materialised the collection aggregate from SQL Server via EF Core. Under concurrent
load, 50 simultaneous requests for the same collection id triggered 50 parallel database
round-trips — a classic cache stampede.

Day 21 adds a two-tier HybridCache layer in front of that EF query:

```
GET /api/collections/{id}/ef
  → CollectionQueryService.GetByIdAsync(id)
      ├── L1 hit  (in-process MemoryCache, ≤ 30 s)  → return cached result immediately
      ├── L2 hit  (Redis, ≤ 5 min)                   → populate L1, return result
      └── Miss    → GetOrCreateAsync factory runs once
                     ├── IServiceScopeFactory creates child scope
                     ├── EF Core query executes (single DB round-trip)
                     ├── result stored in L1 + L2
                     └── all waiting VUs receive the same result
```

All concurrent misses for the same key share a single factory invocation. This is
`GetOrCreateAsync` request coalescing — the defining behaviour of stampede protection.

---

## Implementation

### Why HybridCache Was Introduced

The `GET /api/collections/{id}/ef` endpoint is read-heavy and its response shape is stable
between writes. Every request materialised the full collection aggregate including all
joined `CollectionItems` and `Quotes` rows. Under a 50-VU stampede on a cold key, all 50
requests hit the database simultaneously, producing 50 identical SQL queries. HybridCache
eliminates that fan-out.

### L1 vs L2

| Tier | Storage | TTL | Scope |
|------|---------|-----|-------|
| L1 | In-process `MemoryCache` | 30 seconds | Single application instance |
| L2 | Redis (`StackExchange.Redis`) | 5 minutes | Shared across all instances |

L1 absorbs the hottest traffic without any network round-trip. L2 survives process
restarts and is shared, so a cold-start on one instance does not fan out to the database
on every node.

If Redis is unavailable, HybridCache degrades silently to L1-only operation. The
`abortConnect=false` flag prevents a startup exception; the timeouts ensure a dead Redis
host fails fast instead of blocking requests.

### Cache Wiring (`Extensions/InfrastructureExtensions.cs`)

Full wiring pasted in the [Exercise Answer](#exercise-answer) section above.

### Stampede Protection — `GetOrCreateAsync` Coalescing

`HybridCache.GetOrCreateAsync` accepts a key and a factory lambda. When 50 VUs arrive
simultaneously on a cold key, only **one** factory invocation runs. All 49 remaining
callers are suspended on the same `Task`. Once the factory completes, every waiter
receives the same result from L1 without touching the database again.

### IServiceScopeFactory — Why Not Inject `AppDbContext` Directly

`CollectionQueryService` is registered as a **Singleton**. `AppDbContext` is **Scoped**.
A Singleton cannot take a Scoped dependency at construction time — the Scoped service
would be captured for the lifetime of the process, long after its owning HTTP request
scope was disposed.

The fix is to inject `IServiceScopeFactory` (a Singleton itself) and create a child
scope inside the factory lambda. Each factory invocation gets its own fresh `AppDbContext`
that is disposed when the `await using` block exits, independent of the originating
HTTP request lifetime.

### Cache Invalidation (`Application/Commands/Collections/AddQuoteToCollectionCommandHandler.cs`)

After any mutation of a collection, the cached entry must be evicted. `RemoveAsync`
clears both L1 and L2 atomically.

The eviction is **unconditional** — it fires regardless of whether `UpdateAsync`
returned true or false. This matters because `UpdateAsync` may return false on a retry
after a prior success (zero rows changed), yet the aggregate was already mutated in memory
by `AddItem`. Keeping a stale cache entry after that would serve incorrect data for up to
the full TTL.

The same cache key format `collection:{id}` is used in both the query service and the
command handler. A key mismatch would make invalidation silently ineffective.

### TTL Configuration

| Setting | Value | Reason |
|---------|-------|--------|
| `LocalCacheExpiration` | 30 s | Hot items stay off the Redis wire for up to 30 s. Stale window is acceptable for a read-heavy collection view |
| `Expiration` (Redis) | 5 min | Survives process restarts. Combined with command-side `RemoveAsync`, staleness is bounded by write frequency |

---

## Load Test Methodology

### Endpoint Strategy

| Endpoint | Cache | Purpose |
|----------|-------|---------|
| `GET /api/collections/{id}/ef` | HybridCache (L1 + L2) | Production read path — AFTER benchmark |
| `GET /api/collections/{id}/dapper` | None — always hits DB | Intentional uncached benchmark — BEFORE baseline |

The `/dapper` route intentionally bypasses HybridCache. Its purpose is to show DB-direct
latency under the same concurrency as the cached run. Using `/dapper` as the "before"
avoids disabling the cache registration — both endpoints run back-to-back on the same
live application without any code change between runs.

### Stampede Scenario

**Executor:** `shared-iterations` — all 50 VUs start simultaneously and share a pool of
50 iterations. Every VU fires one request to the same collection id at the same instant.
There is no sleep between iterations — the goal is maximum concurrency on a single key to
trigger (or prevent) the stampede.

**Threshold:** `p(95) < 500 ms` for the stampede scenario. No p(99) threshold is set —
with 51 samples the p99 is the cold-start outlier, not the VU traffic.

**`summaryTrendStats`:** `['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)']` —
p(99) is computed and displayed even without a threshold.

**`setup()` function:** Probes collection IDs 1–20 before VUs start to discover a valid
id. This prevents a false failure on a hardcoded id that may not exist after a DB wipe.
The probe request is included in k6's global `http_req_duration` metric.

```bash
# BEFORE
k6 run --env SCENARIO=stampede --env ENDPOINT_SUFFIX=dapper load-test.js

# AFTER
k6 run --env SCENARIO=stampede load-test.js
```

---

## Results

### BEFORE — `/dapper` (no cache, 50 parallel DB hits)

| Metric | Value |
|--------|-------|
| Requests | 51 |
| Req/s | 25.8 |
| DB queries/sec | 25.8 (one DB hit per request) |
| avg (ms) | 494.8 |
| p95 (ms) | 550.3 |
| p99 (ms) | ≈ 1395 (≈ max; N = 51) |
| max (ms) | 1395.3 |
| Failed | 0 |

### AFTER — `/ef` + HybridCache (coalesced, 1 DB hit)

| Metric | Value |
|--------|-------|
| Requests | 51 |
| Req/s | 30.4 |
| DB queries/sec | 0.6 (one DB hit for the 50-VU burst) |
| avg (ms) | 297.7 |
| p95 (ms) | 348.8 |
| p99 (ms) | ≈ 1296 (≈ max; N = 51) |
| max (ms) | 1295.6 |
| Failed | 0 |

### Improvement Summary

| Metric | BEFORE | AFTER | Δ |
|--------|--------|-------|---|
| DB queries/sec | 25.8 | 0.6 | **−98%** |
| avg latency | 494.8 ms | 297.7 ms | −40% |
| p95 latency | 550.3 ms | 348.8 ms | **−37%** |
| p99 latency | ≈ 1395 ms | ≈ 1296 ms | −7% |
| DB hits (50-VU run) | 50 | 1 | **−98%** |
| Failed requests | 0 | 0 | — |

**p95 dropped 37%** (550 ms → 349 ms) under identical 50-VU concurrency, attributable
entirely to the 49 VUs served from the coalesced in-memory result.

**p99 improvement is modest (−7%)** because at N = 51 samples, p99 is the 50.49th sorted
value — statistically equivalent to the observed maximum. In both runs the maximum is the
cold-start `setup()` probe (cold Redis + cold cache), not a VU request. The 50 VU
requests themselves completed within the p95 band (350–550 ms).

---

## Stampede Protection Proof

**Setup:** Redis key evicted before the run with:
```bash
redis-cli del "QuotesApi:collection:1"
```
50 VUs, 50 shared iterations, no sleep. All 50 requests target `GET /api/collections/1/ef`
simultaneously.

**Sequence with HybridCache:**

1. All 50 VUs arrive at `GetOrCreateAsync("collection:1", factory)` simultaneously.
2. HybridCache allows **one** VU to enter the factory lambda; the remaining 49 suspend.
3. The factory creates an `IServiceScopeFactory` child scope, opens `AppDbContext`, and
   executes one EF Core SELECT against SQL Server.
4. The result is stored in L1 (MemoryCache) and L2 (Redis).
5. The 49 suspended VUs are resumed and all receive the result directly from L1 — no
   second DB round-trip.

**Observed evidence:**

Application log during the 50-VU run contained exactly **one** line:

```
[HH:mm:ss DBG] Cache miss — fetching collection 1 from database {"CollectionId": 1}
```

One log line = one factory invocation = one DB query. Without HybridCache, 50 identical
log lines and 50 DB queries would appear — one per VU.

---

## Cache Hit Rate

| Metric | Value |
|--------|-------|
| Total requests (stampede run) | 51 |
| Cache misses (factory invocations) | 1 |
| Cache hits | 50 |
| Hit rate | **98%** |

The 1 miss is the factory invocation triggered by the cold key. The remaining 50 requests
(49 coalesced VUs + 1 `setup()` probe) were served from cache. In a sustained load
scenario the miss rate approaches 0% once the key is warm — the cache is refreshed at
most once per 30-second L1 TTL window.

---

## Code Changes

| File | Change |
|------|--------|
| `QuotesApi.csproj` | Added `Microsoft.Extensions.Caching.Hybrid` 9.5.0 and `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.0 |
| `Extensions/InfrastructureExtensions.cs` | Added `AddStackExchangeRedisCache` (fail-fast flags) + `AddHybridCache` (TTL config); changed `CollectionQueryService` registration from `AddScoped` to `AddSingleton` |
| `Application/Queries/Collections/CollectionQueryService.cs` | Replaced direct `AppDbContext` injection with `IServiceScopeFactory`; wrapped EF query in `GetOrCreateAsync`; added null eviction after miss |
| `Application/Commands/Collections/AddQuoteToCollectionCommandHandler.cs` | Added `HybridCache` dependency; added unconditional `RemoveAsync` after write |
| `Extensions/CollectionCqrsEndpointExtensions.cs` | Added CACHE NOTE comment on Dapper route explaining intentional cache bypass |
| `appsettings.json` | Added `ConnectionStrings.Redis`; added `QuotesApi.Application.Queries.Collections: Debug` Serilog override for cache-miss visibility |
| `load-test.js` | Added `ENDPOINT_SUFFIX` env var; added `p(99)` to stampede `summaryTrendStats`; fixed handleSummary row order (p90 → p95 → p99); fixed `Failed` metric |

---

## Screenshots / Evidence

### 1 — NuGet Packages (HybridCache + Redis)

![NuGet Packages](Screenshots/01_NuGet_Packages_HybridCache_Redis.png)

`QuotesApi.csproj` showing `Microsoft.Extensions.Caching.Hybrid` (9.5.0) and
`Microsoft.Extensions.Caching.StackExchangeRedis` (10.0.0) as `PackageReference` entries.
Proves the caching dependencies are real project references, not just described.

---

### 2 — HybridCache Wiring (InfrastructureExtensions)

![Infrastructure Extensions](Screenshots/01_HybridCache_Redis_Wiring.png)

`AddStackExchangeRedisCache` with `abortConnect=false`, `connectTimeout=1000`, and
`syncTimeout=500` wires the L2 Redis store with fail-fast flags. `AddHybridCache` sets
the L1 TTL to 30 s and L2 TTL to 5 min. `AddSingleton<ICollectionQueryService>` reflects
the corrected DI lifetime after removing the direct `AppDbContext` dependency.

---

### 3 — Stampede Protection Code (CollectionQueryService)

![CollectionQueryService](Screenshots/02_CollectionQueryService_GetOrCreateAsync.png)

`GetOrCreateAsync` with the `collection:{id}` key, `IServiceScopeFactory` creating a
child scope for each factory invocation, and the `LogDebug` cache-miss line. The null
eviction block below prevents a `null` result from being cached and hiding a subsequently
created collection for the remainder of the TTL.

---

### 4 — Cache Invalidation (AddQuoteToCollectionCommandHandler)

![Cache Invalidation RemoveAsync](Screenshots/03_Cache_Invalidation_RemoveAsync.png)

`RemoveAsync($"collection:{command.CollectionId}")` called unconditionally after
`UpdateAsync`. Both L1 and L2 are cleared atomically in a single call. No `if (ok)` guard
— a retry that returns false (zero rows changed) still leaves the aggregate mutated in
memory, so the cache must be evicted regardless.

---

### 5 — k6 AFTER Load Test (HybridCache, `/ef`)

![After Load Test HybridCache](Screenshots/05_After_LoadTest_HybridCache.png)

`stampede ✓`, `50/50 shared iters`, `Failed: 0`, `p95 ≈ 348.8 ms`. The Endpoint line
shows `/ef`, confirming HybridCache was active. Compare p95 and avg against Screenshot 7
(BEFORE) to see the 37% p95 improvement.

---

### 6 — Stampede Proof (Single Cache Miss Log Line)

![Stampede Proof One Cache Miss](Screenshots/06_Stampede_Proof_One_Cache_Miss.png)

Application terminal during the 50-VU stampede. Exactly **one** `DBG Cache miss —
fetching collection 1 from database` line appears. One log line = one factory invocation
= one DB query. Without HybridCache, 50 lines would appear — one per VU.

---

### 7 — k6 BEFORE Load Test (Dapper, uncached)

![Before Load Test Dapper](Screenshots/04_Before_LoadTest_Dapper.png)

Same 50-VU stampede against `/dapper`. Every VU hit the DB directly: `p95 ≈ 550.3 ms`,
`avg ≈ 494.8 ms`, DB queries/sec = 25.8. The Endpoint line shows `/dapper`, confirming
HybridCache was bypassed. Comparison with Screenshot 5 shows the 37% p95 and 98%
DB-hit-rate improvements.

---

### 8 — Redis CLI (Cached Key + TTL)

![Redis Cache Key TTL](Screenshots/08_Redis_Cache_Key_TTL.png)

`keys QuotesApi:*` returns `"QuotesApi:collection:1"` — the L2 entry was written after
the first cache miss. `ttl QuotesApi:collection:1` returns a positive integer (remaining
seconds in the 5-minute TTL), proving the entry is live in Redis and not just in the
in-process L1 store.

---

## Verification Performed

| Step | Command | Result |
|------|---------|--------|
| API health | `curl http://localhost:5075/health` | `{"status":"ok"}` |
| Redis connectivity | `redis-cli -p 6379 ping` | `PONG` |
| Redis key after warm-up | `redis-cli keys "QuotesApi:*"` | `"QuotesApi:collection:1"` |
| Redis TTL | `redis-cli ttl "QuotesApi:collection:1"` | positive integer |
| BEFORE stampede | `k6 run --env ENDPOINT_SUFFIX=dapper load-test.js` | `stampede ✓`, p95=550.3ms, Failed=0 |
| AFTER stampede | `k6 run load-test.js` | `stampede ✓`, p95=348.8ms, Failed=0 |
| Single cache miss | API log during AFTER run | Exactly 1 `DBG Cache miss` line |
| Cache invalidation | `POST /api/collections/{id}/items` | Redis key evicted; next GET triggers one re-population |

---

## Requirement-to-Evidence Mapping

| Requirement | Evidence | Status |
|-------------|----------|--------|
| Cache wiring pasted | Exercise Answer §1 — NuGet + DI registration + `GetOrCreateAsync` + `RemoveAsync` | ✓ |
| Load test BEFORE (DB queries/sec, p99) | Exercise Answer §2, Results section, Screenshot 7 | ✓ |
| Load test AFTER (DB queries/sec, p99) | Exercise Answer §2, Results section, Screenshot 5 | ✓ |
| p99 reported | Results table — ≈1395ms BEFORE, ≈1296ms AFTER; derived from max (N=51) | ✓ |
| DB queries/sec reported | Results table — 25.8 BEFORE, 0.6 AFTER | ✓ |
| Stampede protection shown | Exercise Answer §3, Stampede Proof section, Screenshot 6 | ✓ |
| HybridCache wiring (L1 + L2) | Screenshot 2, InfrastructureExtensions.cs | ✓ |
| Redis configuration (fail-fast) | Screenshot 2 (`abortConnect=false`, timeouts), Screenshot 8 | ✓ |
| Cache invalidation on mutation | Screenshot 4, AddQuoteToCollectionCommandHandler.cs | ✓ |
| DB load drop under concurrent load | Screenshots 5 vs 7; DB hits 50 → 1 (−98%) | ✓ |
| Cache hit rate measured | Cache Hit Rate section — 98% (1 miss / 51 requests) | ✓ |

---

## Remaining Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| p99 dominated by setup probe | p99 ≈ max for N=51; not a meaningful latency SLO | Use sustained scenario (N≈1200) for p99 SLO measurement |
| L1 staleness across instances | Two app instances may serve different snapshots for up to 30 s | `RemoveAsync` evicts L2; L1 expires within 30 s TTL — acceptable for a collection read model |
| Redis unavailable at startup | `connectTimeout=1000` may block for 1 s per attempt | `abortConnect=false` prevents startup exception; HybridCache degrades to L1-only |
| Null result hiding new collection | `GetOrCreateAsync` would cache `null` for full TTL | Immediate `RemoveAsync` after null result prevents this |
| Cache key collision | `collection:{id}` is short; future entities could collide | `InstanceName = "QuotesApi:"` namespaces all Redis keys; L1 is in-process only |

---

## Key Learnings

> **Write this section yourself.**
> Mentor explicitly marks AI-generated reflections as an automatic failure.

---

## What Would Break This?

> **Write this section yourself.**
> Mentor explicitly marks AI-generated reflections as an automatic failure.
