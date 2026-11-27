// Game analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null, serversChart = null;

    function controls() {
        const r = A.range(document.getElementById('af-range').value);
        const periods = parseInt(document.getElementById('af-compare').value, 10) || 0;
        return {
            gameType: document.getElementById('af-game').value,
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
        const o = await A.fetchJson('/api/Analytics/game/overview' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to }));
        const hasSamples = o.statsSampleCount > 0;
        const hasUnknownSamples = o.statsSampleCount == null;
        A.renderKpis('game-kpis', [
            { label: 'Servers', value: A.fmtNumber(o.serverCount) },
            { label: 'Average sampled occupancy', value: hasSamples || (hasUnknownSamples && o.avgPlayers > 0) ? Math.round(o.avgPlayers * 10) / 10 : '\u2014' },
            { label: 'Peak sampled server occupancy', value: hasSamples || (hasUnknownSamples && o.peakPlayers > 0) ? A.fmtNumber(o.peakPlayers) : '\u2014' },
            { label: 'Latest seen in range', value: A.fmtNumber(o.uniquePlayers) },
            { label: 'Moderation', value: A.fmtNumber(o.moderationActions) }
        ]);
    }

    async function loadTrend(c) {
        const ts = A.maskUnobservedOccupancy(await A.fetchJson('/api/Analytics/game/timeseries' + A.buildQuery(c)));
        ts.series = (ts.series || []).map(function (series) {
            return series.key === 'avgPlayers' ? { ...series, label: 'Average sampled occupancy' } : series;
        });
        trendChart = A.renderComparisonChart({
            canvasId: 'game-trend', fallbackId: 'game-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
    }

    async function loadServers(c) {
        const d = await A.fetchJson('/api/Analytics/game/servers' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 10 }));
        const items = d.items || [];
        serversChart = A.renderBarChart({
            canvasId: 'game-servers', fallbackId: 'game-servers-fallback',
            labels: items.map(function (i) { return i.title; }),
            data: items.map(function (i) { return Math.round(i.avgPlayers * 10) / 10; }),
            label: 'Average sampled occupancy', horizontal: true, chart: serversChart
        });
    }

    async function loadPlayers(c) {
        const d = await A.fetchJson('/api/Analytics/game/players' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 10 }));
        A.renderTable('game-players', ['Player', 'Chat messages'],
            (d.items || []).map(function (i) { return [i.displayName, A.fmtNumber(i.activityCount)]; }));
    }

    async function loadMaps(c) {
        const d = await A.fetchJson('/api/Analytics/game/maps' + A.buildQuery({ gameType: c.gameType, from: c.from, to: c.to, top: 15 }));
        A.renderTable('game-maps', ['Map', 'Samples', 'Up', 'Down', 'Score'],
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
