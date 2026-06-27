// Maps analytics command centre.
(function () {
    'use strict';
    const A = window.Analytics;
    if (!A) { return; }

    let hotspotsChart = null;

    function controls() {
        const r = A.range(document.getElementById('af-range').value);
        const serverEl = document.getElementById('af-server');
        return { from: r.from, to: r.to, serverId: serverEl ? serverEl.value : '' };
    }

    async function loadOverview(c) {
        const o = await A.fetchJson('/api/Analytics/maps/overview' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderKpis('maps-kpis', [
            { label: 'Maps', value: A.fmtNumber(o.totalMaps) },
            { label: 'Plays', value: A.fmtNumber(o.totalPlays) },
            { label: 'Votes', value: A.fmtNumber(o.totalVotes) }
        ]);
    }

    async function loadHotspots(c) {
        const d = await A.fetchJson('/api/Analytics/maps/hotspots' + A.buildQuery({ from: c.from, to: c.to, top: 12 }));
        const items = d.items || [];
        hotspotsChart = A.renderBarChart({
            canvasId: 'maps-hotspots', fallbackId: 'maps-hotspots-fallback',
            labels: items.map(function (i) { return i.mapName; }),
            data: items.map(function (i) { return Math.round(i.avgPlayers * 10) / 10; }),
            label: 'Avg players', horizontal: true, chart: hotspotsChart
        });
    }

    async function loadTopPlayed(c) {
        const d = await A.fetchJson('/api/Analytics/maps/top-played' + A.buildQuery({ from: c.from, to: c.to, top: 15 }));
        A.renderTable('maps-top-played', ['Map', 'Game', 'Plays', 'Share'],
            (d.items || []).map(function (i) { return [i.mapName, i.gameType, A.fmtNumber(i.playsCount), (Math.round(i.sharePercent * 10) / 10) + '%']; }));
    }

    async function loadTopVoted(c) {
        const d = await A.fetchJson('/api/Analytics/maps/top-voted' + A.buildQuery({ from: c.from, to: c.to, top: 15 }));
        A.renderTable('maps-top-voted', ['Map', 'Game', 'Votes'],
            (d.items || []).map(function (i) { return [i.mapName, i.gameType, A.fmtNumber(i.votesCount)]; }));
    }

    async function loadByGame(c) {
        const d = await A.fetchJson('/api/Analytics/maps/by-game' + A.buildQuery({ from: c.from, to: c.to }));
        A.renderTable('maps-by-game', ['Game', 'Maps', 'Plays', 'Votes'],
            (d.items || []).map(function (i) { return [i.gameType, A.fmtNumber(i.mapsPlayed), A.fmtNumber(i.totalPlays), A.fmtNumber(i.totalVotes)]; }));
    }

    async function loadByServer(c) {
        const el = document.getElementById('maps-by-server');
        if (!c.serverId) {
            el.innerHTML = '<p class="text-muted mb-0">Select a server to see its map breakdown.</p>';
            return;
        }
        const d = await A.fetchJson('/api/Analytics/maps/by-server' + A.buildQuery({ id: c.serverId, from: c.from, to: c.to, top: 20 }));
        A.renderTable('maps-by-server', ['Map', 'Plays', 'Avg', 'Share'],
            (d.items || []).map(function (i) { return [i.mapName, A.fmtNumber(i.playsCount), Math.round(i.avgPlayers * 10) / 10, (Math.round(i.sharePercent * 10) / 10) + '%']; }));
    }

    async function refresh() {
        const c = controls();
        await Promise.all([
            loadOverview(c).catch(function () { }),
            loadHotspots(c).catch(function (e) { A.showFallback('maps-hotspots-fallback', String(e)); }),
            loadTopPlayed(c).catch(function () { }),
            loadTopVoted(c).catch(function () { }),
            loadByGame(c).catch(function () { }),
            loadByServer(c).catch(function () { })
        ]);
    }

    document.getElementById('af-refresh').addEventListener('click', refresh);
    const serverSelect = document.getElementById('af-server');
    if (serverSelect) { serverSelect.addEventListener('change', refresh); }
    refresh();
})();
