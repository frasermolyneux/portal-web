// Game analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null, serversChart = null;

    function controls() {
        const r = A.range(document.getElementById('af-range').value);
        return {
            gameType: document.getElementById('af-game').value,
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
        const o = await A.fetchJson('/api/Analytics/game/overview' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to }));
        A.renderKpis('game-kpis', [
            { label: 'Servers', value: A.fmtNumber(o.serverCount) },
            { label: 'Avg players', value: Math.round(o.avgPlayers * 10) / 10 },
            { label: 'Peak players', value: A.fmtNumber(o.peakPlayers) },
            { label: 'Unique players', value: A.fmtNumber(o.uniquePlayers) },
            { label: 'Moderation', value: A.fmtNumber(o.moderationActions) }
        ]);
    }

    async function loadTrend(c) {
        const ts = await A.fetchJson('/api/Analytics/game/timeseries' + A.buildQuery(c));
        trendChart = A.renderComparisonChart({
            canvasId: 'game-trend', fallbackId: 'game-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
        A.renderDeltaSummary('game-trend-summary', ts.summary);
    }

    async function loadServers(c) {
        const d = await A.fetchJson('/api/Analytics/game/servers' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 10 }));
        const items = d.items || [];
        serversChart = A.renderBarChart({
            canvasId: 'game-servers', fallbackId: 'game-servers-fallback',
            labels: items.map(function (i) { return i.title; }),
            data: items.map(function (i) { return Math.round(i.avgPlayers * 10) / 10; }),
            label: 'Avg players', horizontal: true, chart: serversChart
        });
    }

    async function loadPlayers(c) {
        const d = await A.fetchJson('/api/Analytics/game/players' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 10 }));
        A.renderTable('game-players', ['Player', 'Activity'],
            (d.items || []).map(function (i) { return [i.displayName, A.fmtNumber(i.activityCount)]; }));
    }

    async function loadMaps(c) {
        const d = await A.fetchJson('/api/Analytics/game/maps' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 15 }));
        A.renderTable('game-maps', ['Map', 'Plays', 'Up', 'Down', 'Score'],
            (d.items || []).map(function (i) { return [i.mapName, A.fmtNumber(i.plays), A.fmtNumber(i.upVotes), A.fmtNumber(i.downVotes), Math.round(i.score * 10) / 10]; }));
    }

    async function refresh() {
        const c = controls();
        await Promise.all([
            loadOverview(c).catch(function (e) { A.showFallback('game-trend-fallback', String(e)); }),
            loadTrend(c).catch(function (e) { A.showFallback('game-trend-fallback', String(e)); }),
            loadServers(c).catch(function (e) { A.showFallback('game-servers-fallback', String(e)); }),
            loadPlayers(c).catch(function () { }),
            loadMaps(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    document.getElementById('af-game').addEventListener('change', refresh);
    refresh();
})();
