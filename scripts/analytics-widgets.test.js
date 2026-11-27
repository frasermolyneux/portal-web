'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function harness(script, payload, mode = 'light') {
    const calls = [];
    const tables = new Map();
    const fallbacks = new Map();
    const charts = [];
    const kpis = [];
    const nodes = new Map();
    function node(id) {
        if (!nodes.has(id)) {
            nodes.set(id, {
                value: id === 'af-server' ? 'first' : id === 'af-bucket' ? 'OneDay' : '30d',
                dataset: {}, listeners: {}, open: false,
                options: id === 'af-bucket' ? [
                    { value: 'FifteenMinutes', dataset: { maxDays: '3' } },
                    { value: 'OneHour', dataset: { maxDays: '31' } },
                    { value: 'OneDay', dataset: { maxDays: '366' } }
                ] : [],
                addEventListener(event, callback) { this.listeners[event] = callback; },
                replaceChildren() {},
                getAttribute(name) { return name === 'data-server-id' ? 'server' : mode; },
                closest() { return null; }
            });
        }
        return nodes.get(id);
    }
    const analytics = {
        range(preset) {
            const days = { '24h': 1, '7d': 7, '30d': 30, '90d': 90, '180d': 180, '365d': 365 }[preset] || 1;
            const to = new Date('2026-11-02T00:00:00Z');
            return { from: new Date(to.getTime() - days * 86400000).toISOString(), to: to.toISOString() };
        },
        buildQuery(params) { return '?' + new URLSearchParams(params); },
        async fetchJson(url) { calls.push(url); return typeof payload === 'function' ? payload(url) : payload; },
        renderKpis(id, items) { kpis.push(items); },
        renderTable(id, columns, rows) { tables.set(id, { columns, rows }); },
        renderComparisonChart(options) { charts.push(options); return { destroy() {} }; },
        showFallback(id, message) { fallbacks.set(id, message); },
        fmtNumber: String,
        escapeHtml: String
    };
    const context = { window: { Analytics: analytics }, document: { getElementById: node }, URLSearchParams };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '..', 'src', 'XtremeIdiots.Portal.Web', 'wwwroot', 'js', script), 'utf8'), context);
    return { calls, tables, fallbacks, charts, kpis, node };
}

function settled() { return new Promise(resolve => setImmediate(resolve)); }

const recordedActivity = [{
    bucketStartUtc: '2026-11-01T00:00:00Z', bucketEndUtc: '2026-11-02T00:00:00Z',
    recordedPlayers: 7, sessionStarts: 4, observedSeconds: 9000
}];

test('Dashboard uses recorded players and observed interval duration, not legacy presence', async () => {
    const h = harness('analytics-embed-dashboard.js', { recordedActivity, recordedActivityCoverage: 'unknown', trendPoints: [{ value: 999 }], summary: { uniquePlayersCount: 999 } });
    await settled();
    assert.equal(h.calls.length, 1);
    assert.match(h.calls[0], /includeComposition=false/);
    assert.equal(h.charts[0].timezone, 'UTC');
    assert.equal(h.charts[0].timeseries.series[0].values[0].value, 7);
    assert.equal(h.tables.get('dashboard-analytics-activity').rows[0][4], '2.50');
    assert.equal(h.kpis[0][0].value, '4');
    assert.equal(h.node('dashboard-analytics-coverage').textContent, 'Recorded activity coverage: unknown.');
});

test('Old API or absent recording does not render legacy values or invented zero activity', async () => {
    for (const payload of [{ trendPoints: [{ value: 999 }] }, { recordedActivity: [] }, { recordedActivity: null },
        { recordedActivity: [{ ...recordedActivity[0], observedSeconds: undefined }] },
        { recordedActivity: [{ ...recordedActivity[0], observedSeconds: null }] }]) {
        for (const script of ['analytics-embed-dashboard.js', 'analytics-embed-server.js', 'analytics-server.js']) {
            const h = harness(script, payload);
            await settled();
            assert.equal(h.charts.length, 0);
            assert.equal(h.kpis.length, 0);
            assert.match([...h.fallbacks.values()].join(' '), /unavailable|Coverage is not known/);
        }
    }
});

test('Missing or unrecognized coverage never claims complete recording', async () => {
    for (const coverage of [undefined, null, 'complete']) {
        const h = harness('analytics-embed-dashboard.js', { recordedActivity, recordedActivityCoverage: coverage });
        await settled();
        assert.equal(h.node('dashboard-analytics-coverage').textContent, 'Recorded activity coverage: not established.');
    }
});

test('Admin embed initially requests only recorded history, without comparisons; detail requests are lazy and failures explicit', async () => {
    const h = harness('analytics-embed-server.js', url => {
        if (url.includes('timeseries')) { return { recordedActivity, recordedActivityCoverage: 'unknown', bucket: 'OneDay' }; }
        throw new Error('upstream failed');
    }, 'full');
    await settled();
    assert.equal(h.calls.length, 1);
    assert.match(h.calls[0], /compareMode=None/);
    assert.equal(h.charts[0].timeseries.series[0].values[0].value, 7);
    assert.equal(h.tables.get('analytics-embed-server-summary').rows[0][4], '2.50');
    assert.equal(h.node('analytics-embed-server-coverage').textContent, 'Recorded activity coverage: unknown.');
    const details = h.node('analytics-embed-server-details');
    details.open = true;
    details.listeners.toggle();
    await settled();
    assert.equal(h.calls.length, 5);
    for (const id of ['events', 'chat', 'chat-commands', 'maps']) {
        assert.match(h.fallbacks.get('analytics-embed-server-' + id), /Data unavailable/);
        assert.equal(h.tables.has('analytics-embed-server-' + id), false);
    }
    details.listeners.toggle();
    await settled();
    assert.equal(h.calls.length, 5);
});

test('Server selection changes cannot render a late response from the old server', async () => {
    let release;
    const pending = new Promise(resolve => { release = resolve; });
    const h = harness('analytics-server.js', url => url.includes('id=first') ? pending : { recordedActivity, recordedActivityCoverage: 'unknown', bucket: 'OneDay' });
    h.node('af-server').value = 'second';
    h.node('af-server').listeners.change();
    await settled();
    assert.equal(h.charts.length, 1);
    assert.equal(h.calls.filter(url => url.includes('/overview')).length, 0);
    assert.equal(h.calls.length, 10);
    release({ recordedActivity: [{ ...recordedActivity[0], recordedPlayers: 999 }] });
    await settled();
    assert.equal(h.charts.length, 1);
    assert.equal(h.charts[0].timeseries.series[0].values[0].value, 7);
    assert.equal(h.node('server-trend-coverage').textContent, 'Recorded activity coverage: unknown.');
});

test('Server bucket choices follow SDK range limits', async () => {
    const h = harness('analytics-server.js', { recordedActivity, bucket: 'OneDay' });
    await settled();
    const range = h.node('af-range');
    const bucket = h.node('af-bucket');
    assert.equal(bucket.options[0].disabled, true);
    assert.equal(bucket.options[1].disabled, false);
    range.value = '24h';
    range.listeners.change();
    assert.equal(bucket.options[0].disabled, false);
    bucket.value = 'FifteenMinutes';
    range.value = '90d';
    range.listeners.change();
    assert.equal(bucket.value, 'OneDay');
    assert.equal(bucket.options[0].disabled, true);
    assert.equal(bucket.options[1].disabled, true);
    assert.equal(bucket.options[2].disabled, false);
});
