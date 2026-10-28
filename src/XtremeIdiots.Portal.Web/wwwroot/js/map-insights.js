(() => {
    'use strict';
    const root = document.getElementById('mapInsights');
    if (!root) return;
    const el = id => document.getElementById(id);
    const charts = [];
    let data, generation = 0;
    const number = value => new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value);
    const node = (tag, text, className) => {
        const element = document.createElement(tag);
        element.textContent = text;
        if (className) element.className = className;
        return element;
    };
    const families = { 1: 'COD2', 2: 'COD4 / COD4x', 3: 'COD5',
        CallOfDuty2: 'COD2', CallOfDuty4: 'COD4 / COD4x', CallOfDuty5: 'COD5' };
    const name = map => `${map.mapName} (${families[map.gameType] || map.gameType})`;
    function table(target, headers, rows) {
        const container = el(target);
        container.replaceChildren();
        if (!rows.length) { container.append(node('p', 'No matching data in this date range.', 'text-muted')); return; }
        const grid = document.createElement('table');
        grid.className = 'table table-striped';
        const head = grid.createTHead().insertRow();
        headers.forEach(label => {
            const th = node('th', label);
            th.scope = 'col'; head.append(th);
        });
        const body = grid.createTBody();
        rows.forEach(values => {
            const row = body.insertRow();
            values.forEach(value => row.append(node('td', String(value))));
        });
        container.append(grid);
    }
    function rankings() {
        if (!data) return;
        table('insightsRankings', ['Map family / map', 'Likes', 'Dislikes', 'Total votes', 'Approval', 'Evidence'],
            data[el('insightsRanking').value].map(map => [name(map), number(map.likes), number(map.dislikes), number(map.votes),
                `${number(map.approval)}%`, map.votes < data.minimumRankingVotes ? 'Low vote count' : '5+ votes']));
    }
    function chart(id, labels, datasets, percentage = false) {
        charts.push(new Chart(el(id), {
            type: 'bar', data: { labels, datasets },
            options: {
                indexAxis: 'y', responsive: true, maintainAspectRatio: false,
                scales: {
                    x: { beginAtZero: true, max: percentage ? 100 : undefined, stacked: !percentage,
                        title: { display: true, text: percentage ? 'Approval (%)' : 'Recorded votes' },
                        ticks: { precision: 0 } },
                    y: { stacked: !percentage }
                },
                plugins: { legend: { display: !percentage } }
            }
        }));
    }
    function render() {
        charts.splice(0).forEach(item => item.destroy());
        el('insightsTiles').replaceChildren();
        for (const [label, value] of [['Recorded votes', data.totalVotes], ['Likes', data.totalLikes],
            ['Dislikes', data.totalDislikes], ['Approval', data.totalVotes ? `${number(data.approval)}%` : 'Not rated'],
            ['Rated maps', data.ratedMaps]]) {
            const col = node('div', '', 'col-6 col-md');
            const tile = node('div', '', 'ibox-content h-100');
            tile.append(node('p', label, 'small text-muted mb-1'), node('strong', typeof value === 'number' ? number(value) : value, 'h4'));
            col.append(tile); el('insightsTiles').append(col);
        }
        el('insightsResults').hidden = false;
        chart('insightsVolume', data.mostVoted.map(name), [
            { label: 'Likes', data: data.mostVoted.map(map => map.likes), backgroundColor: '#16826c' },
            { label: 'Dislikes', data: data.mostVoted.map(map => map.dislikes), backgroundColor: '#d34a4a' }
        ]);
        chart('insightsApproval', data.popular.map(name), [
            { label: 'Approval (%)', data: data.popular.map(map => map.approval), backgroundColor: '#2274a5' }
        ], true);
        rankings();
        table('insightsHotspots', ['Map family / map', 'Average players', 'Peak players', 'Telemetry samples'],
            data.hotspots.map(map => [name(map), number(map.avgPlayers), number(map.peakPlayers), number(map.sampleCount)]));
    }
    async function load() {
        const current = ++generation;
        el('insightsStatus').textContent = 'Loading map analytics...';
        el('insightsResults').hidden = true;
        const params = new URLSearchParams({ days: el('insightsDays').value });
        if (el('insightsGame').value) params.set('gameType', el('insightsGame').value);
        if (el('insightsServer').value) params.set('gameServerId', el('insightsServer').value);
        try {
            const response = await fetch('/Maps/Insights?' + params, { credentials: 'same-origin' });
            if (!response.ok || response.redirected) throw new Error('Map analytics is temporarily unavailable. Please try again.');
            const result = await response.json();
            if (current !== generation) return;
            data = result; render();
            el('insightsStatus').textContent = data.totalVotes ? 'Showing recorded votes in the selected UTC window.' : 'No votes in this date range.';
        } catch (error) {
            if (current === generation) el('insightsStatus').textContent = error.message;
        }
    }
    el('insightsGame').addEventListener('change', () => {
        const family = el('insightsGame').value;
        for (const option of el('insightsServer').options) {
            option.hidden = !!family && !!option.value && option.dataset.family !== family;
            option.disabled = option.hidden;
        }
        if (el('insightsServer').selectedOptions[0]?.disabled) el('insightsServer').value = '';
    });
    el('mapInsightsFilters').addEventListener('submit', event => { event.preventDefault(); void load(); });
    el('insightsRanking').addEventListener('change', rankings);
    void load();
})();
