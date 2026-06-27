// Server analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null;

    function serverId() {
        const el = document.getElementById('af-server');
        return el ? el.value : '';
    }

    function controls() {
        const r = A.range(document.getElementById('af-range').value);
        return {
            id: serverId(),
            from: r.from,
            to: r.to,
            bucket: document.getElementById('af-bucket').value,
            compareMode: document.getElementById('af-compareMode').value,
            comparePeriods: document.getElementById('af-comparePeriods').value,
            alignMode: document.getElementById('af-alignMode').value,
            normalize: document.getElementById('af-normalize').checked,
            timezone: tz
        };
    }

    async function loadOverview(c) {
        const o = await A.fetchJson('/api/Analytics/server/overview' + A.buildQuery({ id: c.id, from: c.from, to: c.to }));
        A.renderKpis('server-kpis', [
            { label: 'Avg players', value: Math.round(o.avgPlayers * 10) / 10 },
            { label: 'Peak players', value: A.fmtNumber(o.peakPlayers) },
            { label: 'Events', value: A.fmtNumber(o.eventsCount) },
            { label: 'Chat', value: A.fmtNumber(o.chatCount) },
            { label: 'Admin actions', value: A.fmtNumber(o.adminActionsCount) }
        ]);
    }

    async function loadTrend(c) {
        const ts = await A.fetchJson('/api/Analytics/server/timeseries' + A.buildQuery(c));
        trendChart = A.renderComparisonChart({
            canvasId: 'server-trend', fallbackId: 'server-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
        A.renderDeltaSummary('server-trend-summary', ts.summary);
    }

    async function loadLive(c) {
        const d = await A.fetchJson('/api/Analytics/server/players-current' + A.buildQuery({ id: c.id }));
        const badge = d.online
            ? '<span class="badge bg-success">Online</span>'
            : '<span class="badge bg-secondary">Offline</span>';
        const head = '<p class="mb-2">' + badge +
            ' <strong>' + A.fmtNumber(d.currentPlayers) + ' / ' + A.fmtNumber(d.maxPlayers) + '</strong> players' +
            (d.mapName ? ' &middot; <span class="text-muted">' + A.escapeHtml(d.mapName) + '</span>' : '') + '</p>';
        document.getElementById('server-live').innerHTML = head + '<div id="server-live-table"></div>';
        A.renderTable('server-live-table', ['Player', 'Score', 'Ping', 'Team'],
            (d.players || []).map(function (p) { return [p.name || '', A.fmtNumber(p.score), A.fmtNumber(p.ping), p.team || '']; }));
    }

    async function loadEvents(c) {
        const d = await A.fetchJson('/api/Analytics/server/events' + A.buildQuery({ id: c.id, from: c.from, to: c.to }));
        A.renderTable('server-events', ['Event type', 'Count'],
            (d.byType || []).map(function (i) { return [i.eventType, A.fmtNumber(i.count)]; }));
    }

    async function loadChat(c) {
        const d = await A.fetchJson('/api/Analytics/server/chat' + A.buildQuery({ id: c.id, from: c.from, to: c.to, top: 10 }));
        A.renderTable('server-chat', ['Player', 'Messages'],
            (d.topChatters || []).map(function (i) { return [i.username, A.fmtNumber(i.count)]; }));
    }

    async function loadMaps(c) {
        const d = await A.fetchJson('/api/Analytics/server/map-rotation' + A.buildQuery({ id: c.id, from: c.from, to: c.to }));
        A.renderTable('server-maps', ['Map', 'Avg', 'Peak', 'Share'],
            (d.maps || []).map(function (i) { return [i.mapName, Math.round(i.avgPlayers * 10) / 10, A.fmtNumber(i.peakPlayers), (Math.round(i.sharePercent * 10) / 10) + '%']; }));
    }

    async function refresh() {
        const c = controls();
        if (!c.id) { return; }
        await Promise.all([
            loadOverview(c).catch(function (e) { A.showFallback('server-trend-fallback', String(e)); }),
            loadTrend(c).catch(function (e) { A.showFallback('server-trend-fallback', String(e)); }),
            loadLive(c).catch(function () { }),
            loadEvents(c).catch(function () { }),
            loadChat(c).catch(function () { }),
            loadMaps(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    const serverSelect = document.getElementById('af-server');
    if (serverSelect) { serverSelect.addEventListener('change', refresh); }
    refresh();
})();
