(function () {
    'use strict';
    const A = window.Analytics;
    const host = document.getElementById('analytics-embed-player');
    const playerId = host?.dataset.playerId;
    if (!A || !playerId) return;

    const id = suffix => 'analytics-embed-player-' + suffix;
    const el = suffix => document.getElementById(id(suffix));
    const range = el('range');
    const server = el('server');
    const status = el('status');
    const results = el('results');
    const dayLabel = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
    const timestamp = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' });
    let chart;
    let request = 0;

    function utcDate(value) {
        return new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
    }

    function duration(seconds) {
        if (seconds == null) return '\u2014';
        if (seconds === 0) return '0 min';
        const minutes = Math.floor(seconds / 60);
        if (minutes === 0) return '<1 min';
        const hours = Math.floor(minutes / 60);
        return hours > 0 ? `${A.fmtNumber(hours)} h ${minutes % 60} min` : `${minutes} min`;
    }

    function appendRow(table, values) {
        const row = table.insertRow();
        const heading = document.createElement('th');
        heading.scope = 'row';
        heading.textContent = values[0];
        row.append(heading);
        values.slice(1).forEach(value => { row.insertCell().textContent = value; });
        return row;
    }

    function renderDaily(data) {
        const rows = el('daily-rows');
        rows.replaceChildren();
        el('playtime').hidden = data.daily.length === 0;
        if (!data.daily.length) return;

        const recorded = new Map(data.daily.map(day => [day.dateUtc.slice(0, 10), day.measuredSeconds]));
        const labels = [];
        const values = [];
        const start = new Date(data.fromUtc.slice(0, 10) + 'T00:00:00Z').getTime();
        const end = utcDate(data.toUtc).getTime();
        for (let dayMillis = start; dayMillis < end; dayMillis += 24 * 60 * 60 * 1000) {
            const date = new Date(dayMillis);
            const day = date.toISOString().slice(0, 10);
            const seconds = recorded.get(day) ?? null;
            labels.push(dayLabel.format(date));
            values.push(seconds === null ? null : seconds / 60);
            appendRow(rows, [day, duration(seconds)]);
        }

        el('chart').hidden = !window.Chart;
        el('daily').open = !window.Chart;
        if (window.Chart) {
            chart = A.renderBarChart({
                canvasId: id('trend'), labels, data: values, label: 'Play time (min)', singleColor: true
            });
        }
    }

    function renderServers(servers) {
        const rows = el('server-rows');
        rows.replaceChildren();
        el('breakdown').hidden = !!server.value || servers.length === 0;
        if (el('breakdown').hidden) return;

        const max = Math.max(0, ...servers.map(item => item.measuredSeconds));
        for (const item of servers) {
            const row = appendRow(rows, [
                item.serverName,
                duration(item.measuredSeconds || null),
                item.trackedSessions ? A.fmtNumber(item.trackedSessions) : '\u2014'
            ]);
            if (max > 0) {
                const bar = document.createElement('progress');
                bar.className = 'w-100 d-block';
                bar.max = max;
                bar.value = item.measuredSeconds;
                bar.setAttribute('aria-hidden', 'true');
                row.cells[1].append(bar);
            }
        }
    }

    function render(data) {
        const hasMeasured = data.measuredSeconds > 0;
        const count = value => value == null ? '\u2014' : A.fmtNumber(value);
        A.renderKpis(id('kpis'), [
            { label: 'Measured play time', value: duration(hasMeasured ? data.measuredSeconds : null) },
            { label: 'Tracked sessions', value: count(data.trackedSessions || null) },
            { label: 'Active days', value: count(hasMeasured ? data.activeDays : null) },
            { label: 'Recorded chat messages', value: count(data.chatMessages) }
        ]);
        const notes = [];
        if (data.lastObservedUtc) notes.push(`Last observed: ${timestamp.format(utcDate(data.lastObservedUtc))} UTC`);
        if (data.hasOpenSession) notes.push('Open session');
        if (data.unknownMapSeconds > 0) notes.push(`${duration(data.unknownMapSeconds)} without a known map`);
        if (data.chatRetentionCutoffUtc && utcDate(data.fromUtc) < utcDate(data.chatRetentionCutoffUtc)) {
            notes.push('Older chat history is limited');
        }
        if (!hasMeasured) notes.push('No measured play time recorded');
        notes.push('Coverage unknown');
        el('observation').textContent = notes.join(' \u00b7 ');
        el('observation').hidden = notes.length === 0;
        results.hidden = false;
        renderDaily(data);
        renderServers(data.servers);
    }

    async function load() {
        const generation = ++request;
        const serverId = server.value;
        const dates = A.range(range.value);
        results.hidden = true;
        status.hidden = false;
        status.textContent = 'Loading activity\u2026';
        if (chart) {
            chart.destroy();
            chart = null;
        }
        try {
            const response = await A.fetchJson('/api/Analytics/player/' + encodeURIComponent(playerId) + '/activity' +
                A.buildQuery({ from: dates.from, to: dates.to, serverId }));
            if (generation !== request) return;
            const data = response.activity;
            if (!serverId) {
                const options = [...response.availableServers].sort((a, b) => a.serverName.localeCompare(b.serverName))
                    .map(item => new Option(item.serverName, item.serverId));
                server.replaceChildren(new Option('All available servers', ''), ...options);
                server.disabled = options.length === 0;
            }
            if (data.playtimeCoverage?.toLowerCase() === 'unavailable') {
                status.textContent = 'Player activity unavailable.';
                return;
            }
            if (data.playtimeCoverage?.toLowerCase() !== 'unknown') {
                throw new Error('Unrecognized player activity coverage.');
            }
            render(data);
            status.hidden = true;
        } catch (error) {
            if (generation !== request) return;
            console.error('Failed to load player activity.', error);
            results.hidden = true;
            status.textContent = 'Player activity unavailable.';
        }
    }

    range.addEventListener('change', load);
    server.addEventListener('change', load);
    load();
})();
