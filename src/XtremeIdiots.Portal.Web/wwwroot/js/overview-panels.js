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
        var counts = [0, 0, 0, 0];
        var classes = ['bg-success', 'bg-info', 'bg-warning text-dark', 'bg-secondary'];
        var labels = ['Healthy', 'Idle', 'Heartbeat overdue', 'Unknown'];
        document.querySelectorAll('[data-agent-server]').forEach(function (row) {
            var summary = lookup.get(row.dataset.agentServer.toLowerCase());
            var activity = summary ? summary.activityStatus : 3;
            counts[activity]++;
            var badge = document.createElement('span');
            badge.className = 'badge ' + classes[activity];
            badge.textContent = labels[activity];
            row.querySelector('[data-agent-field="status"]').replaceChildren(badge);
            row.querySelector('[data-agent-field="events"]').textContent = summary ? summary.eventsLastHour : 0;
            var heartbeat = summary && summary.lastHeartbeatReceived;
            row.querySelector('[data-agent-field="heartbeat"]').textContent = heartbeat ? utcDate(heartbeat).toLocaleString() : 'Not observed';
            var last = summary && summary.lastEventReceived;
            row.querySelector('[data-agent-field="lastEvent"]').textContent = last ? utcDate(last).toLocaleString() : '—';
        });
        var total = document.getElementById('agent-status-counts');
        if (total) {
            total.replaceChildren();
            counts.forEach(function (count, index) {
                var badge = document.createElement('span');
                badge.className = 'badge me-1 ' + classes[index];
                badge.textContent = count + ' ' + labels[index];
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
                loadOverview(element);
            });
            element.append(retry);
        }
    }

    function agentStatusUnavailable() {
        var counts = document.getElementById('agent-status-counts');
        if (!counts) return;
        counts.replaceChildren();
        document.querySelectorAll('[data-agent-server]').forEach(function (row) {
            row.querySelector('[data-agent-field="status"]').textContent = 'Unavailable';
            row.querySelector('[data-agent-field="events"]').textContent = '—';
            row.querySelector('[data-agent-field="lastEvent"]').textContent = '—';
            row.querySelector('[data-agent-field="heartbeat"]').textContent = '—';
        });
    }

    function loadOverview(element) {
        return window.PortalAsync.loadSection(element, element.dataset.overviewUrl, {
            onLoaded: loaded,
            onError: agentStatusUnavailable
        });
    }

    function loadVisible() {
        document.querySelectorAll('[data-overview-url]').forEach(function (element) {
            var tab = element.closest('.tab-pane');
            if (element.dataset.overviewStarted || (tab && !tab.classList.contains('active'))) return;
            element.dataset.overviewStarted = 'true';
            loadOverview(element);
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
