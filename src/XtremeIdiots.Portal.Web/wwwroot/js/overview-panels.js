(function () {
    'use strict';

    function utcDate(value) {
        return new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z');
    }

    function drawCharts(root) {
        if (!window.google || !google.charts) return;
        var charts = root.querySelectorAll('[data-overview-chart]');
        if (!charts.length) return;
        google.charts.load('current', { packages: ['corechart', 'timeline'] });
        google.charts.setOnLoadCallback(function () {
            charts.forEach(function (container) {
                if (!container.isConnected || !container.offsetWidth || container.dataset.drawn) return;
                var payload = JSON.parse(container.querySelector('[data-chart-data]').textContent);
                var data = new google.visualization.DataTable();
                var chart;
                if (container.dataset.overviewChart === 'timeline') {
                    data.addColumn({ type: 'string', id: 'Map Name' });
                    data.addColumn({ type: 'datetime', id: 'Start' });
                    data.addColumn({ type: 'datetime', id: 'End' });
                    data.addRows(payload.mapTimeline.map(function (point) {
                        return [point.mapName, utcDate(point.start), utcDate(point.end)];
                    }));
                    chart = new google.visualization.Timeline(container);
                    chart.draw(data);
                } else {
                    data.addColumn('datetime', 'Timestamp');
                    data.addColumn('number', 'Player Count');
                    data.addRows(payload.playerCounts.map(function (point) {
                        return [utcDate(point.timestamp), point.playerCount];
                    }));
                    chart = new google.visualization.LineChart(container);
                    chart.draw(data, { title: 'Player Count (Last 48 hours)', curveType: 'function', legend: { position: 'bottom' }, height: 300 });
                }
                container.dataset.drawn = 'true';
            });
        });
    }

    function applyAgentStatus(root) {
        var payload = root.querySelector('[data-agent-status-data]');
        if (!payload) return;
        var summaries = JSON.parse(payload.textContent) || [];
        var lookup = new Map(summaries.map(function (item) { return [item.serverId.toLowerCase(), item]; }));
        var counts = [0, 0, 0];
        document.querySelectorAll('[data-agent-server]').forEach(function (row) {
            var summary = lookup.get(row.dataset.agentServer.toLowerCase());
            // A successful query with no events for this server is an observed offline state.
            var activity = summary ? summary.activityStatus : 2;
            counts[activity]++;
            var badge = document.createElement('span');
            badge.className = 'badge ' + ['bg-success', 'bg-info', 'bg-danger'][activity];
            badge.textContent = ['Active', 'Idle', 'Offline'][activity];
            row.querySelector('[data-agent-field="status"]').replaceChildren(badge);
            row.querySelector('[data-agent-field="events"]').textContent = summary ? summary.eventsLastHour : 0;
            var last = summary && summary.lastEventReceived;
            row.querySelector('[data-agent-field="lastEvent"]').textContent = last ? utcDate(last).toLocaleString() : '—';
        });
        var total = document.getElementById('agent-status-counts');
        if (total) {
            total.replaceChildren();
            counts.forEach(function (count, index) {
                var badge = document.createElement('span');
                badge.className = 'badge me-1 ' + ['bg-success', 'bg-info', 'bg-danger'][index];
                badge.textContent = count + ' ' + ['Active', 'Idle', 'Offline'][index];
                total.append(badge);
            });
        }
    }

    function loaded(element) {
        drawCharts(element);
        applyAgentStatus(element);
        if (element.querySelector('[data-overview-unavailable]')) {
            var retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-secondary mt-2';
            retry.textContent = 'Retry';
            retry.addEventListener('click', function () {
                window.PortalAsync.loadSection(element, element.dataset.overviewUrl, { onLoaded: loaded }).then(function () {
                    if (document.getElementById('agent-status-counts') && !element.querySelector('[data-agent-status-data]')) {
                        document.querySelectorAll('[data-agent-field="status"]').forEach(function (cell) {
                            cell.textContent = 'Unavailable';
                        });
                    }
                });
            });
            element.append(retry);
        }
    }

    function loadVisible() {
        document.querySelectorAll('[data-overview-url]').forEach(function (element) {
            var tab = element.closest('.tab-pane');
            if (element.dataset.overviewStarted || (tab && !tab.classList.contains('active'))) return;
            element.dataset.overviewStarted = 'true';
            window.PortalAsync.loadSection(element, element.dataset.overviewUrl, { onLoaded: loaded });
        });
    }

    document.addEventListener('DOMContentLoaded', loadVisible);
    // Bootstrap emits its jQuery event when jQuery is present.
    if (window.jQuery) {
        window.jQuery(document).on('shown.bs.tab', function () {
            loadVisible();
            drawCharts(document);
        });
    }
    document.addEventListener('shown.bs.tab', function () {
        loadVisible();
        drawCharts(document);
    });
})();
