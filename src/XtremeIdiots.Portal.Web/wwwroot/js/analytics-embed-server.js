// Server page embedded analytics widget. Reads #analytics-embed-server[data-server-id][data-mode].
// mode "full" (server admin detail) adds events/chat/map-rotation; mode "light" (server info) is trend only.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const host = document.getElementById('analytics-embed-server');
    if (!host) { return; }
    const serverId = host.getAttribute('data-server-id');
    const mode = host.getAttribute('data-mode') || 'light';
    if (!serverId) { return; }

    const tz = 'UTC';
    let trendChart = null;

    async function loadTrend() {
        const r = A.range('30d');
        const ts = await A.fetchJson('/api/Analytics/server/timeseries' + A.buildQuery({
            id: serverId, from: r.from, to: r.to, bucket: 'OneDay',
            compareMode: 'None', alignMode: 'None', timezone: tz, normalize: false
        }));
        document.getElementById('analytics-embed-server-coverage').textContent =
            'Recorded activity coverage: ' + (ts.recordedActivityCoverage === 'unknown' ? 'unknown' : 'not established') + '.';
        const points = ts.recordedActivity;
        if (!Array.isArray(points) || !points.length) {
            A.showFallback('analytics-embed-server-trend-fallback', Array.isArray(points)
                ? 'No recorded session activity returned. Coverage is not known.'
                : 'Recorded session activity is unavailable from this API version.');
            return;
        }
        if (points.some(function (p) { return !Number.isFinite(p.observedSeconds) || p.observedSeconds < 0; })) {
            A.showFallback('analytics-embed-server-trend-fallback', 'Observed session duration is unavailable from this API version.');
            return;
        }
        trendChart = A.renderComparisonChart({
            canvasId: 'analytics-embed-server-trend', fallbackId: 'analytics-embed-server-trend-fallback',
            timeseries: { bucket: ts.bucket, labels: points.map(function (p) { return p.bucketStartUtc; }),
                series: [{ label: 'Recorded players with sessions starting or overlapping day (UTC)', role: 'current',
                    values: points.map(function (p) { return { bucketStartUtc: p.bucketStartUtc, value: p.recordedPlayers }; }) }] },
            timezone: tz, index100: false, chart: trendChart
        });
        A.renderTable('analytics-embed-server-summary', ['UTC bucket start', 'UTC bucket end', 'Recorded players', 'Session starts', 'Observed session-interval player-hours'],
            points.map(function (p) { return [p.bucketStartUtc, p.bucketEndUtc, A.fmtNumber(p.recordedPlayers), A.fmtNumber(p.sessionStarts),
                (p.observedSeconds / 3600).toFixed(2)]; }));
    }

    async function loadDetail() {
        const r = A.range('30d');
        const q = A.buildQuery({ id: serverId, from: r.from, to: r.to });
        const chatQuery = A.buildQuery({ id: serverId, from: r.from, to: r.to, top: 10 });
        const commandsQuery = A.buildQuery({ id: serverId, from: r.from, to: r.to, top: 15 });
        async function panel(id, url, columns, rows) {
            try {
                const data = await A.fetchJson(url);
                A.renderTable(id, columns, rows(data));
            } catch (e) {
                A.showFallback(id, 'Data unavailable: ' + String(e));
            }
        }
        await Promise.all([
            panel('analytics-embed-server-events', '/api/Analytics/server/events' + q, ['Event type', 'Count'],
                function (d) { return (d.byType || []).map(function (i) { return [i.eventType, A.fmtNumber(i.count)]; }); }),
            panel('analytics-embed-server-chat', '/api/Analytics/server/chat' + chatQuery, ['Player', 'Messages'],
                function (d) { return (d.topChatters || []).map(function (i) { return [i.username, A.fmtNumber(i.count)]; }); }),
            panel('analytics-embed-server-chat-commands', '/api/Analytics/server/chat-commands' + commandsQuery, ['Command', 'Executed', 'Denied'],
                function (d) { return (d.commands || []).map(function (i) { return [i.command, A.fmtNumber(i.count), A.fmtNumber(i.deniedCount)]; }); }),
            panel('analytics-embed-server-maps', '/api/Analytics/server/map-rotation' + q, ['Map', 'Sample avg players', 'Sample share'],
                function (d) { return (d.maps || []).map(function (i) { return [i.mapName, Math.round(i.avgPlayers * 10) / 10, (Math.round(i.sharePercent * 10) / 10) + '%']; }); })
        ]);
    }

    function run() {
        loadTrend().catch(function (e) { A.showFallback('analytics-embed-server-trend-fallback', String(e)); });
        if (mode === 'full') {
            const details = document.getElementById('analytics-embed-server-details');
            if (details) {
                details.addEventListener('toggle', function () {
                    if (details.open && !details.dataset.loaded) {
                        details.dataset.loaded = 'true';
                        loadDetail();
                    }
                });
            }
        }
    }

    // When embedded inside an inactive tab the chart container has no size at load time, so defer
    // loading until the tab is first shown. Outside a tab (e.g. the server info page) load immediately.
    const tabPane = host.closest('.tab-pane');
    if (tabPane && !tabPane.classList.contains('active')) {
        const trigger = document.querySelector('[data-bs-toggle="tab"][data-bs-target="#' + tabPane.id + '"]');
        if (trigger) {
            if (window.jQuery) {
                window.jQuery(trigger).one('shown.bs.tab', run);
            } else {
                trigger.addEventListener('shown.bs.tab', run, { once: true });
            }
        } else {
            run();
        }
    } else {
        run();
    }
})();
