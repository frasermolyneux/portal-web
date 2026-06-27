// Shared analytics helpers for the Analytics command centres and embedded widgets.
// Backend is always UTC; this layer renders labels/tooltips in the user's locale.
(function () {
    'use strict';

    const PALETTE = ['#1f77b4', '#ff7f0e', '#2ca02c', '#d62728', '#9467bd', '#8c564b', '#e377c2', '#17becf'];
    const MAX_COMPARISON_LINES = 3; // plus the current series = 4 lines max

    function browserTimezone() {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
        } catch (e) {
            return 'UTC';
        }
    }

    function isoUtc(date) {
        return new Date(date).toISOString();
    }

    // preset -> { from, to } as UTC ISO strings. `to` is now.
    function range(preset) {
        const to = new Date();
        const from = new Date(to);
        switch (preset) {
            case '24h': from.setUTCDate(from.getUTCDate() - 1); break;
            case '7d': from.setUTCDate(from.getUTCDate() - 7); break;
            case '30d': from.setUTCDate(from.getUTCDate() - 30); break;
            case '90d': from.setUTCDate(from.getUTCDate() - 90); break;
            case '180d': from.setUTCDate(from.getUTCDate() - 180); break;
            case '365d': from.setUTCDate(from.getUTCDate() - 365); break;
            default: from.setUTCDate(from.getUTCDate() - 7); break;
        }
        return { from: from.toISOString(), to: to.toISOString() };
    }

    function buildQuery(params) {
        const parts = [];
        Object.keys(params || {}).forEach(function (k) {
            const v = params[k];
            if (v === undefined || v === null || v === '') {
                return;
            }
            parts.push(encodeURIComponent(k) + '=' + encodeURIComponent(v));
        });
        return parts.length ? ('?' + parts.join('&')) : '';
    }

    async function fetchJson(url) {
        const resp = await fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' });
        if (!resp.ok) {
            throw new Error('Request failed (' + resp.status + ') for ' + url);
        }
        return resp.json();
    }

    function formatLabel(isoString, bucket, tz) {
        const d = new Date(isoString);
        if (isNaN(d.getTime())) {
            return String(isoString);
        }
        const options = (bucket === 'OneDay')
            ? { year: 'numeric', month: 'short', day: '2-digit' }
            : { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' };
        if (tz && tz !== 'UTC') {
            options.timeZone = tz;
        } else {
            options.timeZone = 'UTC';
        }
        try {
            return new Intl.DateTimeFormat(undefined, options).format(d);
        } catch (e) {
            return d.toISOString();
        }
    }

    function color(i) {
        return PALETTE[i % PALETTE.length];
    }

    function escapeHtml(value) {
        if (value === undefined || value === null) {
            return '';
        }
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function hexToRgba(hex, alpha) {
        const h = hex.replace('#', '');
        const r = parseInt(h.substring(0, 2), 16);
        const g = parseInt(h.substring(2, 4), 16);
        const b = parseInt(h.substring(4, 6), 16);
        return 'rgba(' + r + ',' + g + ',' + b + ',' + alpha + ')';
    }

    function indexTo100(values) {
        const base = values.find(function (v) { return v && v !== 0; });
        if (!base) {
            return values.map(function () { return 0; });
        }
        return values.map(function (v) { return Math.round((v / base) * 1000) / 10; });
    }

    function showFallback(fallbackId, payload) {
        if (!fallbackId) {
            return;
        }
        const el = document.getElementById(fallbackId);
        if (el) {
            el.style.display = 'block';
            el.textContent = typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2);
        }
    }

    function hideFallback(fallbackId) {
        if (!fallbackId) {
            return;
        }
        const el = document.getElementById(fallbackId);
        if (el) {
            el.style.display = 'none';
        }
    }

    // Renders a multi-series comparison line chart from an analytics timeseries DTO
    // ({ bucket, labels:[utcIso], series:[{ key, label, role, periodLabel, values:[{ bucketStartUtc, value }] }] }).
    // The current series renders bold/solid; comparison series render lighter/dashed (max 3).
    function renderComparisonChart(opts) {
        const canvas = document.getElementById(opts.canvasId);
        if (!canvas || !window.Chart) {
            showFallback(opts.fallbackId, 'Chart.js unavailable');
            return null;
        }

        const ts = opts.timeseries || {};
        const bucket = ts.bucket || 'OneDay';
        const tz = opts.timezone || browserTimezone();
        const labels = (ts.labels || []).map(function (l) { return formatLabel(l, bucket, tz); });

        const allSeries = ts.series || [];
        const current = allSeries.filter(function (s) { return (s.role || 'current') === 'current'; });
        const comparison = allSeries.filter(function (s) { return s.role === 'comparison'; }).slice(0, MAX_COMPARISON_LINES);

        if (labels.length === 0 || allSeries.length === 0) {
            showFallback(opts.fallbackId, ts);
            return null;
        }
        hideFallback(opts.fallbackId);

        const normalize = !!opts.index100;
        const datasets = [];

        current.forEach(function (s, i) {
            const raw = (s.values || []).map(function (v) { return v.value; });
            const data = normalize ? indexTo100(raw) : raw;
            datasets.push({
                label: s.label || 'Current',
                data: data,
                borderColor: color(i),
                backgroundColor: hexToRgba(color(i), 0.12),
                borderWidth: 2.5,
                tension: 0.2,
                fill: false,
                pointRadius: 0
            });
        });

        comparison.forEach(function (s, i) {
            const raw = (s.values || []).map(function (v) { return v.value; });
            const data = normalize ? indexTo100(raw) : raw;
            datasets.push({
                label: s.periodLabel || s.label || ('Comparison ' + (i + 1)),
                data: data,
                borderColor: hexToRgba(color(i), 0.55),
                backgroundColor: 'transparent',
                borderWidth: 1.5,
                borderDash: [6, 4],
                tension: 0.2,
                fill: false,
                pointRadius: 0
            });
        });

        if (opts.chart) {
            opts.chart.destroy();
        }

        return new Chart(canvas, {
            type: 'line',
            data: { labels: labels, datasets: datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: true, position: 'bottom' },
                    tooltip: { enabled: true }
                },
                scales: {
                    x: { ticks: { maxRotation: 45, autoSkip: true, maxTicksLimit: 16 } },
                    y: { beginAtZero: !normalize }
                }
            }
        });
    }

    function renderBarChart(opts) {
        const canvas = document.getElementById(opts.canvasId);
        if (!canvas || !window.Chart) {
            showFallback(opts.fallbackId, 'Chart.js unavailable');
            return null;
        }
        const labels = opts.labels || [];
        const data = opts.data || [];
        if (labels.length === 0) {
            showFallback(opts.fallbackId, 'No data');
            return null;
        }
        hideFallback(opts.fallbackId);
        if (opts.chart) {
            opts.chart.destroy();
        }
        return new Chart(canvas, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: opts.label || '',
                    data: data,
                    backgroundColor: labels.map(function (_, i) { return hexToRgba(color(i), 0.6); }),
                    borderColor: labels.map(function (_, i) { return color(i); }),
                    borderWidth: 1
                }]
            },
            options: {
                indexAxis: opts.horizontal ? 'y' : 'x',
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: { x: { beginAtZero: true }, y: { beginAtZero: true } }
            }
        });
    }

    function fmtNumber(n) {
        if (n === null || n === undefined) {
            return '0';
        }
        return Number(n).toLocaleString();
    }

    function renderDeltaSummary(elId, summary) {
        const el = document.getElementById(elId);
        if (!el) {
            return;
        }
        if (!summary) {
            el.innerHTML = '<span class="text-muted">No comparison selected</span>';
            return;
        }
        const delta = Number(summary.delta || 0);
        const pct = Number(summary.deltaPercent || 0);
        const up = delta >= 0;
        const cls = up ? 'text-success' : 'text-danger';
        const icon = up ? 'fa-arrow-trend-up' : 'fa-arrow-trend-down';
        const sign = up ? '+' : '';
        el.innerHTML =
            '<div class="d-flex align-items-baseline gap-3">' +
            '<div><div class="h4 mb-0">' + fmtNumber(summary.currentTotal) + '</div><small class="text-muted">Current total</small></div>' +
            '<div><div class="h6 mb-0 text-muted">' + fmtNumber(summary.baselineTotal) + '</div><small class="text-muted">Baseline</small></div>' +
            '<div class="' + cls + '"><i class="fa-solid fa-fw ' + icon + '" aria-hidden="true"></i> ' +
            sign + fmtNumber(Math.round(delta)) + ' (' + sign + (Math.round(pct * 10) / 10) + '%)</div>' +
            '</div>';
    }

    function renderKpis(containerId, kpis) {
        const el = document.getElementById(containerId);
        if (!el) {
            return;
        }
        el.innerHTML = (kpis || []).map(function (k) {
            return '' +
                '<div class="col">' +
                '  <div class="ibox mb-2"><div class="ibox-content text-center py-3">' +
                '    <div class="h3 mb-0">' + (k.value === undefined ? '-' : escapeHtml(k.value)) + '</div>' +
                '    <small class="text-muted">' + escapeHtml(k.label) + '</small>' +
                '  </div></div>' +
                '</div>';
        }).join('');
    }

    function renderTable(containerId, headers, rows) {
        const el = document.getElementById(containerId);
        if (!el) {
            return;
        }
        if (!rows || rows.length === 0) {
            el.innerHTML = '<p class="text-muted mb-0">No data for the selected range.</p>';
            return;
        }
        const head = '<thead><tr>' + headers.map(function (h) { return '<th>' + escapeHtml(h) + '</th>'; }).join('') + '</tr></thead>';
        const body = '<tbody>' + rows.map(function (r) {
            return '<tr>' + r.map(function (c) { return '<td>' + escapeHtml(c) + '</td>'; }).join('') + '</tr>';
        }).join('') + '</tbody>';
        el.innerHTML = '<table class="table table-striped table-hover mb-0">' + head + body + '</table>';
    }

    window.Analytics = {
        browserTimezone: browserTimezone,
        isoUtc: isoUtc,
        range: range,
        buildQuery: buildQuery,
        fetchJson: fetchJson,
        formatLabel: formatLabel,
        renderComparisonChart: renderComparisonChart,
        renderBarChart: renderBarChart,
        renderDeltaSummary: renderDeltaSummary,
        renderKpis: renderKpis,
        renderTable: renderTable,
        showFallback: showFallback,
        escapeHtml: escapeHtml,
        fmtNumber: fmtNumber
    };
})();
