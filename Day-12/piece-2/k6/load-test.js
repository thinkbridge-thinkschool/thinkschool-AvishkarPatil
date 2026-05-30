/**
 * k6 load test — Day 12 Piece 2: EF Core vs Dapper on the collection read path
 *
 * Run:
 *   k6 run --env BASE_URL=http://localhost:5075 --env COLLECTION_ID=1 k6/load-test.js
 *
 * Four scenarios (two pairs — warmup discarded, measured recorded):
 *   ef_warmup       — 5 VUs × 5 s  (warms JIT + EF compiled-query cache)
 *   ef_scenario     — 20 VUs × 30 s (measured into ef_duration Trend)
 *   dapper_warmup   — 5 VUs × 5 s  (warms JIT + SQL Server plan cache)
 *   dapper_scenario — 20 VUs × 30 s (measured into dapper_duration Trend)
 *
 * Both endpoints return IDENTICAL response payloads — CollectionDetailReadModel.
 * Any latency difference is pure data-access overhead.
 */

import http             from 'k6/http';
import { check, sleep } from 'k6';
import { Trend }        from 'k6/metrics';
import exec             from 'k6/execution';
import { textSummary }  from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

const BASE_URL       = __ENV.BASE_URL       || 'http://localhost:5075';
const COLLECTION_ID  = __ENV.COLLECTION_ID  || '1';

const efDuration     = new Trend('ef_duration',     true);
const dapperDuration = new Trend('dapper_duration', true);

export const options = {
    scenarios: {
        ef_warmup: {
            executor:  'constant-vus',
            exec:      'efTest',
            vus:       5,
            duration:  '5s',
            startTime: '0s',
        },
        ef_scenario: {
            executor:  'constant-vus',
            exec:      'efTest',
            vus:       20,
            duration:  '30s',
            startTime: '5s',
        },
        dapper_warmup: {
            executor:  'constant-vus',
            exec:      'dapperTest',
            vus:       5,
            duration:  '5s',
            startTime: '40s',
        },
        dapper_scenario: {
            executor:  'constant-vus',
            exec:      'dapperTest',
            vus:       20,
            duration:  '30s',
            startTime: '45s',
        },
    },
    thresholds: {
        ef_duration:     ['p(99)<500'],
        dapper_duration: ['p(99)<500'],
    },
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(90)', 'p(95)', 'p(99)'],
};

function hasQuotes(r) {
    if (r.status !== 200 || !r.body) return false;
    try {
        const body = JSON.parse(r.body);
        return Array.isArray(body.quotes);
    } catch (_) {
        return false;
    }
}

function isMeasuredPhase() {
    const name = exec.scenario.name;
    return name === 'ef_scenario' || name === 'dapper_scenario';
}

export function efTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/ef`);
    if (isMeasuredPhase()) efDuration.add(res.timings.duration);
    check(res, {
        'ef: status 200': (r) => r.status === 200,
        'ef: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function dapperTest() {
    const res = http.get(`${BASE_URL}/api/collections/${COLLECTION_ID}/dapper`);
    if (isMeasuredPhase()) dapperDuration.add(res.timings.duration);
    check(res, {
        'dapper: status 200': (r) => r.status === 200,
        'dapper: has quotes': hasQuotes,
    });
    sleep(0.1);
}

export function handleSummary(data) {
    const ef     = data.metrics.ef_duration;
    const dapper = data.metrics.dapper_duration;

    let comparison = '';
    if (ef && dapper && ef.values && dapper.values) {
        const efP50  = ef.values['p(50)']     ?? 0;
        const efP99  = ef.values['p(99)']     ?? 0;
        const dapP50 = dapper.values['p(50)'] ?? 0;
        const dapP99 = dapper.values['p(99)'] ?? 0;

        const lines = [
            '',
            '══ Day-12 Piece-2 — EF Core vs Dapper ══════════════════════',
            `  EF Core   p50 : ${efP50.toFixed(1)} ms`,
            `  EF Core   p99 : ${efP99.toFixed(1)} ms`,
            `  Dapper    p50 : ${dapP50.toFixed(1)} ms`,
            `  Dapper    p99 : ${dapP99.toFixed(1)} ms`,
        ];
        if (efP99 > 0 && dapP99 > 0) {
            const ratio = efP99 / dapP99;
            lines.push(`  p99 ratio (EF/Dapper) : ${ratio.toFixed(2)}×`);
        }
        lines.push(
            '  Note: 5-second warmup discarded before each measured phase.',
            '════════════════════════════════════════════════════════════',
            ''
        );
        comparison = lines.join('\n');
    }

    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }) + comparison,
    };
}
