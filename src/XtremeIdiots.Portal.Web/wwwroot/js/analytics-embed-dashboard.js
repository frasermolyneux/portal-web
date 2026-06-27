// Dashboard embedded analytics widgets (hybrid: layered alongside the legacy dashboard).
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null;

    function compositionTable(containerId, items) {
        A.renderTable(containerId, ['Name', 'Count'],
            (items || []).map(function (i) { return [i.label || i.key, A.fmtNumber(i.count)]; }));
    }

    async function load() {
        const r = A.range('30d');
        try {
            const d = await A.fetchJson('/api/Analytics/dashboard/home' + A.buildQuery({ from: r.from, to: r.to, bucket: 'OneDay', top: 6 }));

            const s = d.summary || {};
            A.renderKpis('dashboard-analytics-kpis', [
                { label: 'Active games', value: A.fmtNumber(s.activeGamesCount) },
                { label: 'Active servers', value: A.fmtNumber(s.activeServersCount) },
                { label: 'Unique players (30d)', value: A.fmtNumber(s.uniquePlayersCount) },
                { label: 'Reports', value: A.fmtNumber(s.reportsCount) }
            ]);

            const points = d.trendPoints || [];
            const series = [{
                key: 'players', label: 'Unique players', role: 'current', periodLabel: '',
                values: points.map(function (p) { return { bucketStartUtc: p.bucketStartUtc, value: p.value }; })
            }];
            trendChart = A.renderComparisonChart({
                canvasId: 'dashboard-analytics-trend', fallbackId: 'dashboard-analytics-trend-fallback',
                timeseries: { bucket: d.bucket || 'OneDay', labels: points.map(function (p) { return p.bucketStartUtc; }), series: series },
                timezone: tz, index100: false, chart: trendChart
            });

            const comp = d.composition || {};
            compositionTable('dashboard-analytics-games', comp.topGames);
            compositionTable('dashboard-analytics-servers', comp.topServers);
            compositionTable('dashboard-analytics-maps', comp.topMaps);
        } catch (e) {
            A.showFallback('dashboard-analytics-trend-fallback', String(e));
        }
    }

    load();
})();
