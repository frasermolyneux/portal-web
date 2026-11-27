// Global analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null, gamesChart = null, serversChart = null;

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
        const o = await A.fetchJson('/api/Analytics/global/overview' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderKpis('global-kpis', [
            { label: 'Online servers', value: A.fmtNumber(o.onlineServers) },
            { label: 'Players online', value: A.fmtNumber(o.totalPlayersOnline) },
            { label: 'Latest seen in range', value: A.fmtNumber(o.uniquePlayersWindow) },
            { label: 'Events', value: A.fmtNumber(o.totalEvents) },
            { label: 'Chat messages', value: A.fmtNumber(o.totalChatMessages) },
            { label: 'Open reports created in range', value: A.fmtNumber(o.openReports) },
            { label: 'Bans active at range end', value: A.fmtNumber(o.activeBans) }
        ]);
    }

    async function loadTrend(c) {
        const ts = A.maskUnobservedOccupancy(await A.fetchJson('/api/Analytics/global/timeseries' + A.buildQuery(c)));
        ts.series = (ts.series || []).map(function (series) {
            const labels = { avgPlayers: 'Average sampled occupancy', peakPlayers: 'Peak sampled server occupancy' };
            return { ...series, label: labels[series.key] || series.label };
        });
        trendChart = A.renderComparisonChart({
            canvasId: 'global-trend', fallbackId: 'global-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
    }

    async function loadGames(c) {
        const d = await A.fetchJson('/api/Analytics/global/games' + A.buildQuery({ from: c.from, to: c.to, top: 8 }));
        const items = d.items || [];
        gamesChart = A.renderBarChart({
            canvasId: 'global-games', fallbackId: 'global-games-fallback',
            labels: items.map(function (i) { return i.gameType; }),
            data: items.map(function (i) { return Math.round(i.avgPlayers * 10) / 10; }),
            label: 'Average sampled occupancy', horizontal: true, chart: gamesChart
        });
    }

    async function loadServers(c) {
        const d = await A.fetchJson('/api/Analytics/global/servers' + A.buildQuery({ from: c.from, to: c.to, top: 8 }));
        const items = d.items || [];
        serversChart = A.renderBarChart({
            canvasId: 'global-servers', fallbackId: 'global-servers-fallback',
            labels: items.map(function (i) { return i.title; }),
            data: items.map(function (i) { return Math.round(i.avgPlayers * 10) / 10; }),
            label: 'Average sampled occupancy', horizontal: true, chart: serversChart
        });
    }

    async function loadPlayers(c) {
        const d = await A.fetchJson('/api/Analytics/global/players' + A.buildQuery({ from: c.from, to: c.to, top: 10 }));
        A.renderTable('global-players', ['Player', 'Game', 'Chat messages'],
            (d.items || []).map(function (i) { return [i.displayName, i.gameType, A.fmtNumber(i.activityCount)]; }));
    }

    async function loadGeo(c) {
        const d = await A.fetchJson('/api/Analytics/global/geo' + A.buildQuery({ from: c.from, to: c.to, top: 10 }));
        A.renderTable('global-geo', ['Country', 'Latest observations'],
            (d.items || []).map(function (i) { return [i.countryCode, A.fmtNumber(i.playerCount)]; }));
    }

    async function loadModeration(c) {
        const d = await A.fetchJson('/api/Analytics/global/moderation' + A.buildQuery({ from: c.from, to: c.to }));
        const s = d.summary || {};
        A.renderTable('global-moderation', ['Metric', 'Value'], [
            ['Total actions', A.fmtNumber(s.totalActions)],
            ['Open reports created in range', A.fmtNumber(s.openReports)],
            ['Closed reports created in range', A.fmtNumber(s.closedReports)]
        ]);
    }

    async function refresh() {
        const c = controls();
        await Promise.all([
            loadOverview(c).catch(function (e) { A.showFallback('global-trend-fallback', String(e)); }),
            loadTrend(c).catch(function (e) { A.showFallback('global-trend-fallback', String(e)); }),
            loadGames(c).catch(function (e) { A.showFallback('global-games-fallback', String(e)); }),
            loadServers(c).catch(function (e) { A.showFallback('global-servers-fallback', String(e)); }),
            loadPlayers(c).catch(function () { }),
            loadGeo(c).catch(function () { }),
            loadModeration(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    refresh();
})();
