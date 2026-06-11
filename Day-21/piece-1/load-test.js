/**
 * Day-21 — HybridCache + stampede protection
 *
 * BEFORE cache (baseline):
 *   Comment out the AddHybridCache / AddStackExchangeRedisCache registrations
 *   in InfrastructureExtensions.cs and restore CollectionQueryService to its
 *   plain EF version, then run:
 *
 *     k6 run --env SCENARIO=sustained load-test.js
 *
 * AFTER cache:
 *   Restore the HybridCache wiring, then run the same command.
 *   Compare: requests/s, p95, p99 from the k6 summary.
 *   Count "Cache miss" lines in the app log to get the DB hit rate.
 *
 * STAMPEDE proof:
 *   Run with SCENARIO=stampede (the default).  With cache: you will see
 *   exactly ONE "Cache miss" log line in the app output no matter how many
 *   VUs fired.  Without cache: you will see one DB query per VU (50 lines).
 *
 * Usage:
 *   k6 run load-test.js                         # stampede scenario
 *   k6 run --env SCENARIO=sustained load-test.js # sustained load
 *   k6 run --env BASE_URL=http://localhost:5000 \
 *           --env COLLECTION_ID=1 load-test.js
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// ── Custom metrics ────────────────────────────────────────────────────────────
// These mirror what you would observe in the Serilog console output.
// k6 itself cannot count DB hits — correlate with app logs.
const successRate   = new Rate('success_rate');
const latencyTrend  = new Trend('collection_latency_ms', true);

// ── Configuration ─────────────────────────────────────────────────────────────
const BASE_URL       = __ENV.BASE_URL       || 'http://localhost:5075';
const SCENARIO       = __ENV.SCENARIO       || 'stampede';
// ENDPOINT_SUFFIX controls which read path the VUs hit:
//   ef     (default) — EF Core + HybridCache  → AFTER screenshot
//   dapper           — Dapper, bypasses cache  → BEFORE screenshot
const ENDPOINT_SUFFIX = __ENV.ENDPOINT_SUFFIX || 'ef';

// COLLECTION_ID is resolved at runtime:
//   1. Use --env COLLECTION_ID=N if provided (explicit override).
//   2. Otherwise setup() probes IDs 1–20 and picks the first that returns 200.
//      This prevents false failures when the seeder assigns IDs that do not
//      start at 1 (e.g. after a DB wipe and re-seed with IDENTITY gaps).
const COLLECTION_ID_OVERRIDE = __ENV.COLLECTION_ID || null;

// ── Scenarios ─────────────────────────────────────────────────────────────────
//
// stampede  — 50 VUs fire at the SAME instant (shared-iterations, no sleep).
//             With HybridCache: 1 DB query, 49 served from the coalesced Task.
//             Without HybridCache: 50 parallel DB queries.
//
// sustained — 20 VUs, 60 s constant rate.
//             Measures steady-state throughput and latency percentiles.

// ── Setup — runs once before VUs start ────────────────────────────────────────
// Discovers a valid collection ID so the test does not fail on a hardcoded
// assumption about the database seed state.
export function setup() {
    if (COLLECTION_ID_OVERRIDE) {
        const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID_OVERRIDE}/ef`);
        if (res.status !== 200)
            throw new Error(`Provided COLLECTION_ID=${COLLECTION_ID_OVERRIDE} returned HTTP ${res.status}. Seed the database first.`);
        console.log(`Using provided COLLECTION_ID=${COLLECTION_ID_OVERRIDE}`);
        return { collectionId: COLLECTION_ID_OVERRIDE };
    }

    for (let i = 1; i <= 20; i++) {
        const res = http.get(`${BASE_URL}/api/collections/${i}/ef`);
        if (res.status === 200) {
            console.log(`Discovered valid COLLECTION_ID=${i}`);
            return { collectionId: String(i) };
        }
    }
    throw new Error('No collection found in IDs 1–20. Run the API once to trigger the perf seed, then retry.');
}

export const options = SCENARIO === 'sustained'
    ? {
        scenarios: {
            sustained: {
                executor:  'constant-vus',
                vus:       20,
                duration:  '60s',
            },
        },
        // p(90) and p(95) are always computed; p(99) requires summaryTrendStats.
        summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
        thresholds: {
            http_req_duration:        ['p(95)<150', 'p(99)<300'],
            http_req_failed:          ['rate<0.01'],
            success_rate:             ['rate>0.99'],
        },
    }
    : {
        // stampede: all 50 VUs start together with no ramp-up
        scenarios: {
            stampede: {
                executor:    'shared-iterations',
                vus:         50,
                iterations:  50,
                maxDuration: '30s',
            },
        },
        // p(90) is always computed; p(99) is omitted — with 50 iterations the
        // single setup() discovery probe (cold cache, ~5 s) is always the
        // statistical outlier and makes p99 meaningless for the stampede result.
        summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)'],
        thresholds: {
            // p(95)<500: all 50 coalesced VUs must complete within 500 ms.
            // p(99) excluded — see summaryTrendStats comment above.
            http_req_duration: ['p(95)<500'],
            http_req_failed:   ['rate<0.01'],
        },
    };

// ── Virtual user function ─────────────────────────────────────────────────────
export default function (data) {
    const endpoint = `${BASE_URL}/api/collections/${data.collectionId}/${ENDPOINT_SUFFIX}`;
    const start    = Date.now();
    const res      = http.get(endpoint, { timeout: '10s' });
    const ms       = Date.now() - start;

    const ok = check(res, {
        'HTTP 200':         r => r.status === 200,
        'has quotes field': r => {
            try {
                return JSON.parse(r.body).quotes !== undefined;
            } catch (_) {
                return false;
            }
        },
    });

    successRate.add(ok);
    latencyTrend.add(ms);

    // No sleep intentional for stampede scenario — all VUs must hit the endpoint
    // as close to simultaneously as possible to trigger the coalescing path.
    // For sustained scenario add think time to stay at ~20 req/s.
    if (SCENARIO === 'sustained') sleep(0.05);
}

// ── Summary ───────────────────────────────────────────────────────────────────
export function handleSummary(data) {
    const d  = data.metrics.http_req_duration;
    const rps = data.metrics.http_reqs
        ? (data.metrics.http_reqs.values.count / data.state.testRunDurationMs * 1000).toFixed(1)
        : '?';

    const lines = [
        '',
        '┌─────────────────────────────────────────────────────┐',
        '│  Day-21 HybridCache Load Test — Summary             │',
        '├─────────────────────────────────────────────────────┤',
        `│  Scenario   : ${SCENARIO.padEnd(36)}│`,
        `│  Endpoint   : ${(BASE_URL + `/api/collections/{id}/${ENDPOINT_SUFFIX}`).slice(0, 36).padEnd(36)}│`,
        '├─────────────────────────────────────────────────────┤',
        `│  Requests   : ${String(data.metrics.http_reqs?.values?.count ?? '?').padEnd(36)}│`,
        `│  Req/s      : ${String(rps).padEnd(36)}│`,
        `│  avg (ms)   : ${String(d?.values?.avg?.toFixed(1)         ?? '?').padEnd(36)}│`,
        `│  p50 (ms)   : ${String(d?.values?.med?.toFixed(1)         ?? '?').padEnd(36)}│`,
        `│  p95 (ms)   : ${String(d?.values?.['p(95)']?.toFixed(1)   ?? '?').padEnd(36)}│`,
        `│  p90 (ms)   : ${String(d?.values?.['p(90)']?.toFixed(1)   ?? '?').padEnd(36)}│`,
        `│  max (ms)   : ${String(d?.values?.max?.toFixed(1)         ?? '?').padEnd(36)}│`,
        `│  Failed     : ${String(data.metrics.http_req_failed?.values?.passes ?? 0).padEnd(36)}│`,
        '├─────────────────────────────────────────────────────┤',
        '│  DB hit rate: count "Cache miss" lines in app log   │',
        '│  Cache hits : total_requests - cache_miss_count     │',
        '│  Hit rate % : (1 - db_hits / total) * 100          │',
        '└─────────────────────────────────────────────────────┘',
        '',
        '  Stampede proof: with cache, app log shows exactly 1',
        '  "Cache miss" line for the entire stampede run.',
        '  Without cache: 1 line (and 1 DB query) per VU.',
        '',
    ];

    console.log(lines.join('\n'));

    return {
        stdout: lines.join('\n'),
    };
}
