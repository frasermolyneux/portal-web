// Player profile embedded analytics widget. Reads #analytics-embed-player[data-player-id].
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const host = document.getElementById('analytics-embed-player');
    if (!host) { return; }
    const playerId = host.getAttribute('data-player-id');
    if (!playerId) { return; }

    const tz = A.browserTimezone();
    let trendChart = null;

    async function load() {
        const r = A.range('90d');
        try {
            const detail = await A.fetchJson('/api/Analytics/player/' + encodeURIComponent(playerId) + '/detail' + A.buildQuery({ from: r.from, to: r.to }));
            const mod = detail.moderation || {};
            const rel = detail.related || {};
            A.renderKpis('analytics-embed-player-kpis', [
                { label: 'Sessions (90d)', value: A.fmtNumber(detail.sessionsCount) },
                { label: 'Play time (min)', value: A.fmtNumber(detail.totalPlayTimeMinutes) },
                { label: 'Warnings', value: A.fmtNumber(mod.warningsCount) },
                { label: 'Kicks', value: A.fmtNumber(mod.kicksCount) },
                { label: 'Bans', value: A.fmtNumber(mod.bansCount) },
                { label: 'Related players', value: A.fmtNumber(rel.relatedPlayersCount) }
            ]);

            const ts = await A.fetchJson('/api/Analytics/player/' + encodeURIComponent(playerId) + '/timeseries' + A.buildQuery({
                from: r.from, to: r.to, bucket: 'OneDay',
                compareMode: 'PreviousPeriod', comparePeriods: 1, alignMode: 'None', timezone: tz, normalize: false
            }));
            trendChart = A.renderComparisonChart({
                canvasId: 'analytics-embed-player-trend', fallbackId: 'analytics-embed-player-trend-fallback',
                timeseries: ts, timezone: tz, index100: false, chart: trendChart
            });
            A.renderDeltaSummary('analytics-embed-player-summary', ts.summary);
        } catch (e) {
            A.showFallback('analytics-embed-player-trend-fallback', String(e));
        }
    }

    load();
})();
