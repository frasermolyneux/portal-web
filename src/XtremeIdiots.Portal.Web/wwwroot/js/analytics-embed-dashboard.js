// Dashboard embedded analytics widgets (hybrid: layered alongside the legacy dashboard).
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = 'UTC';
    let trendChart = null;

    async function load() {
        const r = A.range('30d');
        try {
            const d = await A.fetchJson('/api/Analytics/dashboard/home' + A.buildQuery({ from: r.from, to: r.to, bucket: 'OneDay', top: 6 }));

            const points = d.recordedActivity;
            if (!Array.isArray(points)) {
                A.showFallback('dashboard-analytics-trend-fallback', 'Recorded session activity is unavailable from this API version.');
                return;
            }
            if (!points.length) {
                A.showFallback('dashboard-analytics-trend-fallback', 'No recorded session activity was returned. Coverage is not known.');
                return;
            }
            if (points.some(function (p) { return !Number.isFinite(p.observedSeconds) || p.observedSeconds < 0; })) {
                A.showFallback('dashboard-analytics-trend-fallback', 'Observed session duration is unavailable from this API version.');
                return;
            }
            A.renderKpis('dashboard-analytics-kpis', [
                { label: 'Recorded session starts (30d)', value: A.fmtNumber(points.reduce(function (sum, p) { return sum + p.sessionStarts; }, 0)) },
                { label: 'Observed session-interval player-hours (30d)', value: (points.reduce(function (sum, p) { return sum + p.observedSeconds; }, 0) / 3600).toFixed(1) }
            ]);

            const series = [{
                key: 'players', label: 'Recorded players overlapping day (UTC)', role: 'current', periodLabel: '',
                values: points.map(function (p) { return { bucketStartUtc: p.bucketStartUtc, value: p.recordedPlayers }; })
            }];
            trendChart = A.renderComparisonChart({
                canvasId: 'dashboard-analytics-trend', fallbackId: 'dashboard-analytics-trend-fallback',
                timeseries: { bucket: d.bucket || 'OneDay', labels: points.map(function (p) { return p.bucketStartUtc; }), series: series },
                timezone: tz, index100: false, chart: trendChart
            });

            A.renderTable('dashboard-analytics-activity', ['UTC bucket start', 'UTC bucket end', 'Recorded players', 'Session starts', 'Observed session-interval player-hours'],
                points.map(function (p) { return [p.bucketStartUtc, p.bucketEndUtc, A.fmtNumber(p.recordedPlayers), A.fmtNumber(p.sessionStarts),
                    (p.observedSeconds / 3600).toFixed(2)]; }));
        } catch (e) {
            A.showFallback('dashboard-analytics-trend-fallback', String(e));
        }
    }

    load();
})();
