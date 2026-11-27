// Server analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = 'UTC';
    let trendChart = null;
    let generation = 0;

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
            compareMode: 'None',
            alignMode: 'None',
            normalize: false,
            timezone: tz,
            generation: generation
        };
    }

    async function loadTrend(c) {
        const query = Object.assign({}, c);
        delete query.generation;
        const ts = await A.fetchJson('/api/Analytics/server/timeseries' + A.buildQuery(query));
        if (c.generation !== generation) { return; }
        document.getElementById('server-trend-coverage').textContent =
            'Recorded activity coverage: ' + (ts.recordedActivityCoverage === 'unknown' ? 'unknown' : 'not established') + '.';
        const points = ts.recordedActivity;
        if (!Array.isArray(points) || !points.length) {
            A.showFallback('server-trend-fallback', Array.isArray(points)
                ? 'No recorded session activity returned. Coverage is not known.'
                : 'Recorded session activity is unavailable from this API version.');
            return;
        }
        if (points.some(function (p) { return !Number.isFinite(p.observedSeconds) || p.observedSeconds < 0; })) {
            A.showFallback('server-trend-fallback', 'Observed session duration is unavailable from this API version.');
            return;
        }
        trendChart = A.renderComparisonChart({
            canvasId: 'server-trend', fallbackId: 'server-trend-fallback',
            timeseries: { bucket: ts.bucket, labels: points.map(function (p) { return p.bucketStartUtc; }),
                series: [{ label: 'Recorded players overlapping bucket (UTC)', role: 'current',
                    values: points.map(function (p) { return { bucketStartUtc: p.bucketStartUtc, value: p.recordedPlayers }; }) }] },
            timezone: tz, index100: false, chart: trendChart
        });
        A.renderTable('server-trend-summary', ['UTC bucket start', 'UTC bucket end', 'Recorded players', 'Session starts', 'Observed session-interval player-hours'],
            points.map(function (p) { return [p.bucketStartUtc, p.bucketEndUtc, A.fmtNumber(p.recordedPlayers), A.fmtNumber(p.sessionStarts),
                (p.observedSeconds / 3600).toFixed(2)]; }));
    }

    async function loadLive(c) {
        const d = await A.fetchJson('/api/Analytics/server/players-current' + A.buildQuery({ id: c.id }));
        if (c.generation !== generation) { return; }
        if (!d.lastUpdatedUtc) {
            A.showFallback('server-live', 'Live status has not been observed.');
            return;
        }
        const badge = d.online
            ? '<span class="badge bg-success">Online</span>'
            : '<span class="badge bg-secondary">Offline</span>';
        const head = '<p class="text-muted small">Status snapshot updated ' + A.escapeHtml(d.lastUpdatedUtc) + ' (UTC). Not historical activity.</p><p class="mb-2">' + badge +
            ' <strong>' + A.fmtNumber(d.currentPlayers) + ' / ' + A.fmtNumber(d.maxPlayers) + '</strong> players' +
            (d.mapName ? ' &middot; <span class="text-muted">' + A.escapeHtml(d.mapName) + '</span>' : '') + '</p>';
        document.getElementById('server-live').innerHTML = head + '<div id="server-live-table"></div>';
        A.renderTable('server-live-table', ['Player', 'Score', 'Ping', 'Team'],
            (d.players || []).map(function (p) { return [p.name || '', A.fmtNumber(p.score), A.fmtNumber(p.ping), p.team || '']; }));
    }

    async function loadEvents(c) {
        const d = await A.fetchJson('/api/Analytics/server/events' + A.buildQuery({ id: c.id, from: c.from, to: c.to }));
        if (c.generation !== generation) { return; }
        A.renderTable('server-events', ['Event type', 'Count'],
            (d.byType || []).map(function (i) { return [i.eventType, A.fmtNumber(i.count)]; }));
    }

    async function loadChat(c) {
        const d = await A.fetchJson('/api/Analytics/server/chat' + A.buildQuery({ id: c.id, from: c.from, to: c.to, top: 10 }));
        if (c.generation !== generation) { return; }
        A.renderTable('server-chat', ['Player', 'Messages'],
            (d.topChatters || []).map(function (i) { return [i.username, A.fmtNumber(i.count)]; }));
    }

    async function loadMaps(c) {
        const d = await A.fetchJson('/api/Analytics/server/map-rotation' + A.buildQuery({ id: c.id, from: c.from, to: c.to }));
        if (c.generation !== generation) { return; }
        A.renderTable('server-maps', ['Map', 'Sample avg players', 'Sample peak', 'Sample share'],
            (d.maps || []).map(function (i) { return [i.mapName, Math.round(i.avgPlayers * 10) / 10, A.fmtNumber(i.peakPlayers), (Math.round(i.sharePercent * 10) / 10) + '%']; }));
    }

    async function refresh() {
        generation++;
        const c = controls();
        if (!c.id) { return; }
        if (trendChart) { trendChart.destroy(); trendChart = null; }
        ['server-live', 'server-events', 'server-chat', 'server-maps', 'server-trend-summary', 'server-trend-coverage'].forEach(function (id) {
            document.getElementById(id).replaceChildren();
        });
        A.showFallback('server-trend-fallback', 'Loading recorded activity...');
        const button = document.getElementById('af-refresh');
        button.disabled = true;
        function failed(id, e) {
            if (c.generation === generation) { A.showFallback(id, 'Data unavailable: ' + String(e)); }
        }
        await Promise.all([
            loadTrend(c).catch(function (e) { failed('server-trend-fallback', e); }),
            loadLive(c).catch(function (e) { failed('server-live', e); }),
            loadEvents(c).catch(function (e) { failed('server-events', e); }),
            loadChat(c).catch(function (e) { failed('server-chat', e); }),
            loadMaps(c).catch(function (e) { failed('server-maps', e); })
        ]);
        if (c.generation === generation) { button.disabled = false; }
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    const serverSelect = document.getElementById('af-server');
    if (serverSelect) { serverSelect.addEventListener('change', refresh); }
    refresh();
})();
