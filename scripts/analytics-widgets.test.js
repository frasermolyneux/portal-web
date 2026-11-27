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
                addEventListener(event, callback) { this.listeners[event] = callback; },
                replaceChildren() {},
                getAttribute(name) { return name === 'data-server-id' ? 'server' : mode; },
                closest() { return null; }
            });
        }
        return nodes.get(id);
    }
    const analytics = {
        range() { return { from: '2026-11-01T00:00:00Z', to: '2026-11-02T00:00:00Z' }; },
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
    recordedPlayers: 7, sessionStarts: 4, measuredSeconds: 7200, unknownMapSeconds: 1800
}];

test('Dashboard uses recorded players and separate known/unknown time, not legacy presence', async () => {
    const h = harness('analytics-embed-dashboard.js', { recordedActivity, trendPoints: [{ value: 999 }], summary: { uniquePlayersCount: 999 } });
    await settled();
    assert.equal(h.calls.length, 1);
    assert.equal(h.charts[0].timezone, 'UTC');
    assert.equal(h.charts[0].timeseries.series[0].values[0].value, 7);
    assert.equal(h.tables.get('dashboard-analytics-activity').rows[0][4], '2.00');
    assert.equal(h.tables.get('dashboard-analytics-activity').rows[0][5], '0.50');
    assert.equal(h.kpis[0][0].value, '4');
});

test('Old API or absent recording does not render legacy values or invented zero activity', async () => {
    for (const payload of [{ trendPoints: [{ value: 999 }] }, { recordedActivity: [] }, { recordedActivity: null }]) {
        for (const script of ['analytics-embed-dashboard.js', 'analytics-embed-server.js', 'analytics-server.js']) {
            const h = harness(script, payload);
            await settled();
            assert.equal(h.charts.length, 0);
            assert.equal(h.kpis.length, 0);
            assert.match([...h.fallbacks.values()].join(' '), /unavailable|Coverage is not known/);
        }
    }
});

test('Admin embed initially requests only recorded history, without comparisons; detail requests are lazy and failures explicit', async () => {
    const h = harness('analytics-embed-server.js', url => {
        if (url.includes('timeseries')) { return { recordedActivity, bucket: 'OneDay' }; }
        throw new Error('upstream failed');
    }, 'full');
    await settled();
    assert.equal(h.calls.length, 1);
    assert.match(h.calls[0], /compareMode=None/);
    assert.equal(h.charts[0].timeseries.series[0].values[0].value, 7);
    assert.equal(h.tables.get('analytics-embed-server-summary').rows[0][5], '0.50');
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
    const h = harness('analytics-server.js', url => url.includes('id=first') ? pending : { recordedActivity, bucket: 'OneDay' });
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
});
