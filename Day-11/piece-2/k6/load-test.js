/**
 * k6 load test — Day 11 Piece 2: Slow (N+1) vs Optimized (single SQL + index)
 *
 * Run:
 *   k6 run --env BASE_URL=http://localhost:5075 k6/load-test.js
 *
 * Four scenarios, two pairs:
 *   slow_warmup       — 5 VUs × 5 s  (discarded; warms JIT + plan cache + pool)
 *   slow_scenario     — 20 VUs × 30 s (measured into slow_duration)
 *   optimized_warmup  — 5 VUs × 5 s  (discarded)
 *   optimized_scenario — 20 VUs × 30 s (measured into optimized_duration)
 *
 * The warmup pattern is standard for benchmark methodology — without it the
 * first ~50 requests of each measured scenario pay JIT compilation + SQL
 * Server query-plan compilation + ASP.NET Core connection-pool growth, which
 * shows up as inflated p99 tail latency.  Discarding those iterations is how
 * you measure steady-state behaviour.
 *
 * Target: optimized p99 ≤ slow p99 / 10  (i.e. a 10× drop on the same workload).
 */

import http               from 'k6/http';
import { check, sleep }   from 'k6';
import { Trend }          from 'k6/metrics';
import exec               from 'k6/execution';
import { textSummary }    from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

const BASE_URL      = __ENV.BASE_URL      || 'http://localhost:5075';
const COLLECTION_ID = __ENV.COLLECTION_ID || '1';

// ── Custom metrics ─────────────────────────────────────────────────────────
// Only the measured scenarios record into these; warmup iterations are
// dropped on the floor.  See isMeasuredPhase() below.
const slowDuration      = new Trend('slow_duration',      true);
const optimizedDuration = new Trend('optimized_duration', true);

export const options = {
    scenarios: {
        // ── Phase 1: SLOW warmup (discarded) ────────────────────────────
        slow_warmup: {
            executor:  'constant-vus',
            exec:      'slowTest',
            vus:       5,
            duration:  '5s',
            startTime: '0s',
        },
        // ── Phase 2: SLOW measured ──────────────────────────────────────
        slow_scenario: {
            executor:  'constant-vus',
            exec:      'slowTest',
            vus:       20,
            duration:  '30s',
            startTime: '5s',
        },
        // ── Phase 3: OPTIMIZED warmup (discarded) ───────────────────────
        // 5-second gap (35 → 40 s) before warmup so slow's logs settle.
        optimized_warmup: {
            executor:  'constant-vus',
            exec:      'optimizedTest',
            vus:       5,
            duration:  '5s',
            startTime: '40s',
        },
        // ── Phase 4: OPTIMIZED measured ─────────────────────────────────
        optimized_scenario: {
            executor:  'constant-vus',
            exec:      'optimizedTest',
            vus:       20,
            duration:  '30s',
            startTime: '45s',
        },
    },
    thresholds: {
        // Slow endpoint: EXPECTED to fail — documents the baseline problem.
        slow_duration:      ['p(50)<2000', 'p(99)<8000'],
        // Optimized endpoint: target the 10× drop on p99.
        // Piece-1 baseline slow p99 ≈ 4000 ms.  10× drop → ≤ 400 ms.
        optimized_duration: ['p(50)<200',  'p(99)<400'],
    },

    // Default Trend stats omit p(50) and p(99) — add them explicitly.
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(90)', 'p(95)', 'p(99)'],
};

function hasQuotes(r) {
    if (r.status !== 200 || !r.body) return false;
    try {
        const body = JSON.parse(r.body);
        return body.quotes && body.quotes.length > 0;
    } catch (_) {
        return false;
    }
}

// Only record latency during the MEASURED scenarios, not the warmup ones.
function isMeasuredPhase() {
    const name = exec.scenario.name;
    return name === 'slow_scenario' || name === 'optimized_scenario';
}

export function slowTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/quotes/slow`);
    if (isMeasuredPhase()) {
        slowDuration.add(res.timings.duration);
    }
    check(res, {
        'slow: status 200': (r) => r.status === 200,
        'slow: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function optimizedTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/quotes/optimized`);
    if (isMeasuredPhase()) {
        optimizedDuration.add(res.timings.duration);
    }
    check(res, {
        'optimized: status 200': (r) => r.status === 200,
        'optimized: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function handleSummary(data) {
    const slow = data.metrics.slow_duration;
    const opt  = data.metrics.optimized_duration;

    let comparison = '';
    if (slow && opt && slow.values && opt.values) {
        const slowP50 = slow.values['p(50)'] ?? slow.values.med ?? 0;
        const slowP99 = slow.values['p(99)'] ?? 0;
        const optP50  = opt.values['p(50)']  ?? opt.values.med  ?? 0;
        const optP99  = opt.values['p(99)']  ?? 0;

        const lines = [
            '',
            '══ Day-11 Piece-2 — Before vs After ════════════════════════',
            `  BEFORE  (slow, N+1)         p50 : ${slowP50.toFixed(1)} ms`,
            `  BEFORE  (slow, N+1)         p99 : ${slowP99.toFixed(1)} ms`,
            `  AFTER   (optimized + index) p50 : ${optP50.toFixed(1)} ms`,
            `  AFTER   (optimized + index) p99 : ${optP99.toFixed(1)} ms`,
        ];
        if (optP99 > 0) {
            const ratio = slowP99 / optP99;
            const verdict = ratio >= 10
                ? '✓ MEETS 10× target'
                : `✗ BELOW 10× target (${ratio.toFixed(1)}×)`;
            lines.push(`  p99 drop ratio              : ${ratio.toFixed(1)}×  ${verdict}`);
        }
        lines.push('  Note: 5-second warmup discarded before each measured phase.');
        lines.push('════════════════════════════════════════════════════════════', '');
        comparison = lines.join('\n');
    }

    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }) + comparison,
    };
}
