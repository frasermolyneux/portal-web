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

    const tz = A.browserTimezone();
    let trendChart = null;

    async function loadTrend() {
        const r = A.range('30d');
        const ts = await A.fetchJson('/api/Analytics/server/timeseries' + A.buildQuery({
            id: serverId, from: r.from, to: r.to, bucket: 'OneDay',
            compareMode: 'PreviousPeriod', comparePeriods: 1, alignMode: 'None', timezone: tz, normalize: false
        }));
        trendChart = A.renderComparisonChart({
            canvasId: 'analytics-embed-server-trend', fallbackId: 'analytics-embed-server-trend-fallback',
            timeseries: ts, timezone: tz, index100: false, chart: trendChart
        });
        A.renderDeltaSummary('analytics-embed-server-summary', ts.summary);
    }

    async function loadDetail() {
        const r = A.range('30d');
        const q = A.buildQuery({ id: serverId, from: r.from, to: r.to });
        const [events, chat, maps] = await Promise.all([
            A.fetchJson('/api/Analytics/server/events' + q).catch(function () { return { byType: [] }; }),
            A.fetchJson('/api/Analytics/server/chat' + A.buildQuery({ id: serverId, from: r.from, to: r.to, top: 10 })).catch(function () { return { topChatters: [] }; }),
            A.fetchJson('/api/Analytics/server/map-rotation' + q).catch(function () { return { maps: [] }; })
        ]);
        A.renderTable('analytics-embed-server-events', ['Event type', 'Count'],
            (events.byType || []).map(function (i) { return [i.eventType, A.fmtNumber(i.count)]; }));
        A.renderTable('analytics-embed-server-chat', ['Player', 'Messages'],
            (chat.topChatters || []).map(function (i) { return [i.username, A.fmtNumber(i.count)]; }));
        A.renderTable('analytics-embed-server-maps', ['Map', 'Avg', 'Share'],
            (maps.maps || []).map(function (i) { return [i.mapName, Math.round(i.avgPlayers * 10) / 10, (Math.round(i.sharePercent * 10) / 10) + '%']; }));
    }

    loadTrend().catch(function (e) { A.showFallback('analytics-embed-server-trend-fallback', String(e)); });
    if (mode === 'full') {
        loadDetail();
    }
})();
