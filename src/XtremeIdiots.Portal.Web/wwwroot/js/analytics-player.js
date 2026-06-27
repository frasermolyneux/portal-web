// Player analytics command centre (aggregate).
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null;

    function controls() {
        const r = A.range(document.getElementById('af-range').value);
        const periods = parseInt(document.getElementById('af-compare').value, 10) || 0;
        return {
            from: r.from,
            to: r.to,
            bucket: document.getElementById('af-bucket').value,
            compareMode: periods > 0 ? 'RollingPeriods' : 'None',
            comparePeriods: periods > 0 ? periods : 1,
            alignMode: document.getElementById('af-alignMode').value,
            normalize: document.getElementById('af-normalize').checked,
            timezone: tz
        };
    }

    async function loadOverview(c) {
        const o = await A.fetchJson('/api/Analytics/player/overview' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderKpis('player-kpis', [
            { label: 'Active players', value: A.fmtNumber(o.activePlayers) },
            { label: 'New players', value: A.fmtNumber(o.newPlayers) },
            { label: 'Returning', value: A.fmtNumber(o.returningPlayers) },
            { label: 'Total', value: A.fmtNumber(o.totalPlayers) }
        ]);
    }

    async function loadTrend(c) {
        const ts = await A.fetchJson('/api/Analytics/player/timeseries' + A.buildQuery(c));
        trendChart = A.renderComparisonChart({
            canvasId: 'player-trend', fallbackId: 'player-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
        A.renderDeltaSummary('player-trend-summary', ts.summary);
    }

    async function loadTop(c) {
        const d = await A.fetchJson('/api/Analytics/player/top' + A.buildQuery({ from: c.from, to: c.to, top: 15 }));
        A.renderTable('player-top', ['Player', 'Game', 'Sessions'],
            (d.items || []).map(function (i) { return [i.username, i.gameType, A.fmtNumber(i.sessionsCount)]; }));
    }

    async function loadByGame(c) {
        const d = await A.fetchJson('/api/Analytics/player/by-game' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderTable('player-by-game', ['Game', 'Total', 'New', 'Active'],
            (d.items || []).map(function (i) { return [i.gameType, A.fmtNumber(i.totalPlayers), A.fmtNumber(i.newPlayers), A.fmtNumber(i.activePlayers)]; }));
    }

    async function loadByServer(c) {
        const d = await A.fetchJson('/api/Analytics/player/by-server' + A.buildQuery({ from: c.from, to: c.to, top: 20 }));
        A.renderTable('player-by-server', ['Server', 'Game', 'Active players'],
            (d.items || []).map(function (i) { return [i.title, i.gameType, A.fmtNumber(i.activePlayers)]; }));
    }

    async function refresh() {
        const c = controls();
        await Promise.all([
            loadOverview(c).catch(function (e) { A.showFallback('player-trend-fallback', String(e)); }),
            loadTrend(c).catch(function (e) { A.showFallback('player-trend-fallback', String(e)); }),
            loadTop(c).catch(function () { }),
            loadByGame(c).catch(function () { }),
            loadByServer(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    refresh();
})();
