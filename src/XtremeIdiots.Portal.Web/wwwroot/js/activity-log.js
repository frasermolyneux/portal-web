$(document).ready(function () {
    const tableEl = $('#dataTable');
    const timeRangeSel = document.getElementById('filterTimeRange');
    const categorySel = document.getElementById('filterCategory');
    const eventNameSel = document.getElementById('filterEventName');
    const includeReadsCb = document.getElementById('filterIncludeReads');

    const categoryBadgeClasses = {
        'Authentication': 'text-bg-primary',
        'Authorization': 'text-bg-danger',
        'AdminActions': 'text-bg-warning',
        'PlayerManagement': 'text-bg-info',
        'GameServers': 'text-bg-success',
        'Credentials': 'text-bg-danger',
        'BanFileMonitors': 'text-bg-secondary',
        'Demos': 'text-bg-secondary',
        'Maps': 'text-bg-info',
        'UserManagement': 'text-bg-primary',
        'Tags': 'text-bg-secondary',
        'ProtectedNames': 'text-bg-warning',
        'Chat': 'text-bg-info',
        'Notifications': 'text-bg-secondary',
        'System': 'text-bg-dark'
    };

    function renderCategoryBadge(category) {
        const cls = categoryBadgeClasses[category] || 'text-bg-secondary';
        return '<span class="badge ' + cls + '">' + category + '</span>';
    }

    function renderTimestamp(data) {
        if (!data) return '';
        var d = new Date(data);
        var now = new Date();
        var diff = Math.floor((now - d) / 1000);
        var label;

        if (diff < 60) label = diff + 's ago';
        else if (diff < 3600) label = Math.floor(diff / 60) + 'm ago';
        else if (diff < 86400) label = Math.floor(diff / 3600) + 'h ago';
        else label = Math.floor(diff / 86400) + 'd ago';

        return '<span title="' + d.toISOString() + '">' + label + '</span>';
    }

    function renderProperties(properties) {
        if (!properties || Object.keys(properties).length === 0) return '';

        var items = [];
        for (var key in properties) {
            if (properties.hasOwnProperty(key)) {
                items.push('<small><strong>' + escapeHtml(key) + ':</strong> ' + escapeHtml(properties[key]) + '</small>');
            }
        }

        return '<details><summary class="text-muted"><small>' + items.length + ' properties</small></summary>' +
            '<div class="mt-1">' + items.join('<br>') + '</div></details>';
    }

    function escapeHtml(str) {
        if (!str) return '';
        var div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function renderSource(data, type, row) {
        var parts = [];
        if (row.controller) parts.push(row.controller);
        if (row.action) parts.push(row.action);
        return parts.length > 0 ? '<small>' + escapeHtml(parts.join('/')) + '</small>' : '';
    }

    function getSelectedValues(selectEl) {
        if (!selectEl) return [];
        return Array.from(selectEl.selectedOptions).map(function (o) { return o.value; });
    }

    function setSelectedValues(selectEl, values) {
        if (!selectEl || !values) return;
        var valSet = new Set(Array.isArray(values) ? values : []);
        Array.from(selectEl.options).forEach(function (o) {
            o.selected = valSet.has(o.value);
        });
    }

    // DataTable initialization
    var table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        deferLoading: 0,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[0, 'desc']],
        stateSaveParams: function (settings, data) {
            data._activityLogVersion = 2;
            if (timeRangeSel) data.timeRange = timeRangeSel.value;
            if (categorySel) data.categories = getSelectedValues(categorySel);
            if (eventNameSel) data.eventNames = getSelectedValues(eventNameSel);
            if (includeReadsCb) data.includeReads = includeReadsCb.checked;
        },
        stateLoadParams: function (settings, data) {
            if (data._activityLogVersion !== 2) return false;
            if (timeRangeSel && data.timeRange) timeRangeSel.value = data.timeRange;
            if (categorySel && data.categories) setSelectedValues(categorySel, data.categories);
            if (includeReadsCb && typeof data.includeReads !== 'undefined') includeReadsCb.checked = data.includeReads;
        },
        columnDefs: [
            { targets: 0, responsivePriority: 1 },
            { targets: 1, responsivePriority: 3 },
            { targets: 2, responsivePriority: 2 },
            { targets: 3, responsivePriority: 1 },
            { targets: 4, responsivePriority: 5 },
            { targets: 5, responsivePriority: 4 }
        ],
        ajax: {
            url: '/User/GetActivityLogAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);

                var baseUrl = '/User/GetActivityLogAjax';
                var qs = [];

                var tr = timeRangeSel?.value;
                if (tr) qs.push('timeRange=' + encodeURIComponent(tr));

                var cats = getSelectedValues(categorySel);
                if (cats.length) qs.push('categories=' + encodeURIComponent(cats.join(',')));

                var evts = getSelectedValues(eventNameSel);
                if (evts.length) qs.push('eventNames=' + encodeURIComponent(evts.join(',')));

                if (includeReadsCb?.checked) qs.push('includeReads=true');

                this.url = baseUrl + (qs.length ? ('?' + qs.join('&')) : '');
            }
        },
        columns: [
            {
                data: 'timestamp', name: 'timestamp', orderable: true,
                render: function (data) { return renderTimestamp(data); }
            },
            {
                data: 'category', name: 'category', orderable: false,
                render: function (data) { return renderCategoryBadge(data); }
            },
            {
                data: 'eventName', name: 'eventname', orderable: true,
                render: function (data) { return '<code>' + escapeHtml(data) + '</code>'; }
            },
            {
                data: 'username', name: 'username', orderable: true,
                render: function (data, type, row) {
                    if (!data) return '<span class="text-muted">—</span>';
                    return escapeHtml(data);
                }
            },
            {
                data: null, name: 'controller', orderable: true,
                render: renderSource
            },
            {
                data: 'properties', name: 'properties', orderable: false,
                render: function (data) { return renderProperties(data); }
            }
        ]
    });

    // Cascading category → event name filter with AbortController for race safety
    var eventNamesController = null;

    function loadEventNames(selectedValues, triggerDraw) {
        if (eventNamesController) {
            eventNamesController.abort();
        }
        eventNamesController = new AbortController();

        var cats = getSelectedValues(categorySel);
        var includeReads = includeReadsCb?.checked || false;
        var url = '/User/GetActivityLogEvents?includeReads=' + includeReads;
        if (cats.length) url += '&categories=' + encodeURIComponent(cats.join(','));

        fetch(url, { signal: eventNamesController.signal })
            .then(function (r) { return r.json(); })
            .then(function (events) {
                eventNameSel.innerHTML = '';
                events.forEach(function (evt) {
                    var opt = document.createElement('option');
                    opt.value = evt.name;
                    opt.textContent = evt.name;
                    eventNameSel.appendChild(opt);
                });
                if (selectedValues && selectedValues.length) {
                    setSelectedValues(eventNameSel, selectedValues);
                }
                if (triggerDraw) {
                    table.page('first').draw(false);
                }
            })
            .catch(function (err) {
                if (err.name === 'AbortError') return;
                console.error('Failed to load event names:', err);
                if (triggerDraw) {
                    table.page('first').draw(false);
                }
            });
    }

    // Load initial event names from saved state, then trigger first draw
    var savedState = table.state();
    loadEventNames(savedState?.eventNames || [], true);

    // Filter change handlers
    timeRangeSel?.addEventListener('change', function () {
        table.page('first').draw(false);
    });

    categorySel?.addEventListener('change', function () {
        loadEventNames([], false);
        table.page('first').draw(false);
    });

    eventNameSel?.addEventListener('change', function () {
        table.page('first').draw(false);
    });

    includeReadsCb?.addEventListener('change', function () {
        loadEventNames([], false);
        table.page('first').draw(false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        timeRangeSel.value = '24h';
        setSelectedValues(categorySel, []);
        includeReadsCb.checked = false;
        table.search('');
        loadEventNames([], false);
        table.page('first').draw(false);
    });
});
