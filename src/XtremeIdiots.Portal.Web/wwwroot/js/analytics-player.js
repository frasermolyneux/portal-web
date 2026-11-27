// Player analytics command centre (aggregate).
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    const tz = A.browserTimezone();
    let trendChart = null;
    let overviewHasTracking = false;
    let trendHasTracking = false;

    function updateTrackingCoverage() {
        document.getElementById('player-tracking-coverage').hidden = !(overviewHasTracking || trendHasTracking);
    }

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
        const kpis = [
            { label: 'Latest seen in range', value: A.fmtNumber(o.activePlayers) },
            { label: 'First seen in range', value: A.fmtNumber(o.newPlayers) },
            { label: 'Latest seen in range; first seen earlier', value: A.fmtNumber(o.returningPlayers) }
        ];
        if (Number.isFinite(o.observedPlayerRecords)) {
            kpis.push({ label: 'Player records first seen before range end', value: A.fmtNumber(o.observedPlayerRecords) });
        }
        const hasTracking = o.trackingCoverage === 'unknown';
        if (hasTracking && Number.isFinite(o.recordedPlayers)) {
            kpis.push({ label: 'Players in tracked sessions', value: A.fmtNumber(o.recordedPlayers) });
        }
        if (hasTracking && Number.isFinite(o.sessionStarts)) {
            kpis.push({ label: 'Tracked session starts', value: A.fmtNumber(o.sessionStarts) });
        }
        overviewHasTracking = hasTracking && (Number.isFinite(o.recordedPlayers) || Number.isFinite(o.sessionStarts));
        updateTrackingCoverage();
        A.renderKpis('player-kpis', kpis);
    }

    async function loadTrend(c) {
        const ts = await A.fetchJson('/api/Analytics/player/timeseries' + A.buildQuery(c));
        ts.series = (ts.series || []).map(function (series) {
            const labels = { newPlayers: 'First seen', activePlayers: 'Latest seen' };
            return {
                ...series,
                label: labels[series.key] || series.label,
                periodLabel: series.role === 'comparison' ? 'Latest seen: ' + series.periodLabel : series.periodLabel
            };
        });
        const hasTrackedStarts = ts.trackingCoverage === 'unknown' && ts.points?.length > 0 &&
            ts.points.every(function (point) { return Number.isFinite(point.sessionStarts); });
        trendHasTracking = hasTrackedStarts;
        updateTrackingCoverage();
        document.getElementById('player-trend-title').textContent = hasTrackedStarts
            ? 'First, latest and tracked session starts' : 'First and latest observations';
        if (hasTrackedStarts) {
            ts.series.push({
                key: 'sessionStarts',
                label: 'Tracked session starts (coverage unknown)',
                values: ts.points.map(function (point) {
                    return { bucketStartUtc: point.bucketStartUtc, value: point.sessionStarts };
                })
            });
        }
        trendChart = A.renderComparisonChart({
            canvasId: 'player-trend', fallbackId: 'player-trend-fallback',
            timeseries: ts, timezone: tz, index100: c.normalize, chart: trendChart
        });
    }

    async function loadByGame(c) {
        const d = await A.fetchJson('/api/Analytics/player/by-game' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderTable('player-by-game', ['Game', 'First and latest seen in range', 'Latest seen in range'],
            (d.items || []).map(function (i) { return [i.gameType, A.fmtNumber(i.newPlayers), A.fmtNumber(i.activePlayers)]; }));
    }

    async function loadByServer(c) {
        const d = await A.fetchJson('/api/Analytics/player/by-server' + A.buildQuery({ from: c.from, to: c.to, top: 20 }));
        A.renderTable('player-by-server', ['Server', 'Game', 'Latest observations'],
            (d.items || []).map(function (i) { return [i.title, i.gameType, A.fmtNumber(i.activePlayers)]; }));
    }

    async function refresh() {
        const c = controls();
        await Promise.all([
            loadOverview(c).catch(function (e) { A.showFallback('player-trend-fallback', String(e)); }),
            loadTrend(c).catch(function (e) { A.showFallback('player-trend-fallback', String(e)); }),
            loadByGame(c).catch(function () { }),
            loadByServer(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    refresh();
})();
