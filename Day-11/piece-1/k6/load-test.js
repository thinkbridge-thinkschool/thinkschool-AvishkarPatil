/**
 * k6 load test — Day 11 Piece 1: Slow vs Fast endpoint comparison
 *
 * Run:
 *   k6 run k6/load-test.js
 *
 * Two back-to-back scenarios:
 *   slow_scenario — hits the N+1 endpoint for 30 s with 20 VUs
 *   fast_scenario — hits the optimised endpoint for 30 s with 20 VUs
 *                   starts after a 5-second gap so logs are clearly separated
 *
 * What to record for the exercise:
 *   p50 and p99 from BOTH scenarios (shown in the k6 summary at the end).
 *   The delta between slow and fast p99 is the cost of N+1 + missing index.
 *
 * Implementation note:
 *   Per-scenario percentiles use CUSTOM Trend metrics rather than tagged
 *   http_req_duration submetrics.  In k6 v0.50+ scenario tags do not always
 *   reliably surface percentile values in the summary, so we record the
 *   request duration directly into our own metrics and read those.
 */

import http               from 'k6/http';
import { check, sleep }   from 'k6';
import { Trend }          from 'k6/metrics';
import { textSummary }    from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

const BASE_URL      = __ENV.BASE_URL      || 'http://localhost:5000';
const COLLECTION_ID = __ENV.COLLECTION_ID || '1';

// ── Custom metrics ─────────────────────────────────────────────────────────
// One Trend per endpoint so the summary can show p50/p99 separately.
const slowDuration = new Trend('slow_duration', true); // true = treat as time
const fastDuration = new Trend('fast_duration', true);

export const options = {
    scenarios: {
        slow_scenario: {
            executor:  'constant-vus',
            exec:      'slowTest',
            vus:       20,
            duration:  '30s',
            startTime: '0s',
        },
        fast_scenario: {
            executor:  'constant-vus',
            exec:      'fastTest',
            vus:       20,
            duration:  '30s',
            startTime: '35s',   // 5-second gap after slow finishes
        },
    },
    thresholds: {
        // Slow endpoint: we EXPECT these to fail — documents the baseline problem
        slow_duration: ['p(50)<2000', 'p(99)<8000'],
        // Fast endpoint: should be dramatically better
        fast_duration: ['p(50)<100',  'p(99)<500'],
    },

    // Default Trend stats omit p(50) and p(99) — add them explicitly so the
    // summary and our handleSummary() can read them.
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(90)', 'p(95)', 'p(99)'],
};

function hasQuotes(r) {
    // Guard JSON.parse so a 404 / empty body doesn't crash the VU iteration with
    // "Unexpected end of JSON input".  We want the check to *fail*, not throw.
    if (r.status !== 200 || !r.body) return false;
    try {
        const body = JSON.parse(r.body);
        return body.quotes && body.quotes.length > 0;
    } catch (_) {
        return false;
    }
}

export function slowTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/quotes/slow`);
    slowDuration.add(res.timings.duration);
    check(res, {
        'slow: status 200': (r) => r.status === 200,
        'slow: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function fastTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/quotes/fast`);
    fastDuration.add(res.timings.duration);
    check(res, {
        'fast: status 200': (r) => r.status === 200,
        'fast: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function handleSummary(data) {
    const slow = data.metrics.slow_duration;
    const fast = data.metrics.fast_duration;

    let comparison = '';
    if (slow && fast && slow.values && fast.values) {
        const slowP50 = slow.values['p(50)'] ?? slow.values.med ?? 0;
        const slowP99 = slow.values['p(99)'] ?? 0;
        const fastP50 = fast.values['p(50)'] ?? fast.values.med ?? 0;
        const fastP99 = fast.values['p(99)'] ?? 0;

        const lines = [
            '',
            '══ Performance Comparison ══════════════════════════════',
            `  Slow (N+1)   p50 : ${slowP50.toFixed(1)} ms`,
            `  Slow (N+1)   p99 : ${slowP99.toFixed(1)} ms`,
            `  Fast (batch) p50 : ${fastP50.toFixed(1)} ms`,
            `  Fast (batch) p99 : ${fastP99.toFixed(1)} ms`,
        ];
        if (fastP99 > 0) {
            lines.push(`  p99 ratio (slow/fast) : ${(slowP99 / fastP99).toFixed(1)}×`);
        }
        lines.push('════════════════════════════════════════════════════════', '');
        comparison = lines.join('\n');
    }

    // Keep the standard k6 summary AND append our comparison block.
    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }) + comparison,
    };
}
