$(document).ready(function () {
    var tableEl = $('#dataTable');
    var gameTypeSel = document.getElementById('filterGameType');
    var serverSel = document.getElementById('filterGameServer');
    var allServers = [];

    function escapeHtml(str) {
        if (!str) return '';
        var div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function renderEventData(data) {
        if (!data) return '';
        try {
            var parsed = JSON.parse(data);
            var items = [];
            for (var key in parsed) {
                if (parsed.hasOwnProperty(key)) {
                    items.push('<small><strong>' + escapeHtml(key) + ':</strong> ' + escapeHtml(String(parsed[key])) + '</small>');
                }
            }
            if (items.length === 0) return '<small class="text-muted">' + escapeHtml(data) + '</small>';
            return '<details><summary class="text-muted"><small>' + items.length + ' fields</small></summary>' +
                '<div class="mt-1">' + items.join('<br>') + '</div></details>';
        } catch (e) {
            return '<small class="text-muted">' + escapeHtml(data) + '</small>';
        }
    }

    var table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 700,
        stateSave: true,
        deferLoading: 0,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[0, 'desc']],
        stateSaveParams: function (settings, data) {
            data._serverEventsVersion = 1;
            if (gameTypeSel) data.gameType = gameTypeSel.value || '';
            if (serverSel) data.serverId = serverSel.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._serverEventsVersion !== 1) return false;
            if (gameTypeSel && data.gameType) gameTypeSel.value = data.gameType;
            if (serverSel && typeof data.serverId !== 'undefined') serverSel._pendingValue = data.serverId;
        },
        columnDefs: [
            { targets: 0, responsivePriority: 1 },
            { targets: 1, responsivePriority: 2 },
            { targets: 2, responsivePriority: 3 },
            { targets: 3, responsivePriority: 4 }
        ],
        ajax: {
            url: '/ServerAdmin/GetServerEventsAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);

                var baseUrl = '/ServerAdmin/GetServerEventsAjax';
                var qs = [];

                var gt = gameTypeSel?.value;
                if (gt) qs.push('gameType=' + encodeURIComponent(gt));

                var srv = serverSel?.value;
                if (srv) qs.push('gameServerId=' + encodeURIComponent(srv));

                this.url = baseUrl + (qs.length ? ('?' + qs.join('&')) : '');
            }
        },
        columns: [
            {
                data: 'timestamp', name: 'timestamp', orderable: true,
                render: function (data) {
                    if (!data) return '';
                    return '<span title="' + data + '">' + formatDateTime(data, { showRelative: true }) + '</span>';
                }
            },
            {
                data: 'eventType', name: 'eventType', orderable: false,
                render: function (data) {
                    return '<code>' + escapeHtml(data) + '</code>';
                }
            },
            {
                data: null, name: 'serverName', orderable: false,
                render: function (data, type, row) {
                    return escapeHtml(row['gameServer']?.['liveTitle'] || row['gameServer']?.['title'] || '');
                }
            },
            {
                data: 'eventData', name: 'eventData', orderable: false,
                render: function (data) { return renderEventData(data); }
            }
        ]
    });

    // Server filter population
    function populateServers() {
        if (!serverSel) return;
        fetch('/ServerAdmin/GetGameServers', { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json().catch(function () { return []; }) : []; })
            .then(function (list) {
                if (!Array.isArray(list)) return;
                allServers = list;
                rebuildServerOptions();
                if (serverSel._pendingValue) {
                    serverSel.value = serverSel._pendingValue;
                    delete serverSel._pendingValue;
                }
                table.page('first').draw(false);
            })
            .catch(function () {
                table.page('first').draw(false);
            });
    }

    function rebuildServerOptions() {
        if (!serverSel) return;
        var current = serverSel.value;
        serverSel.innerHTML = '<option value="">All Servers</option>';
        var gtFilter = gameTypeSel?.value || '';
        allServers
            .filter(function (s) { return !gtFilter || s.gameType === gtFilter; })
            .forEach(function (s) {
                var opt = document.createElement('option');
                opt.value = s.id;
                opt.textContent = s.title;
                serverSel.appendChild(opt);
            });
        if (current && Array.from(serverSel.options).some(function (o) { return o.value === current; })) {
            serverSel.value = current;
        }
    }

    populateServers();

    // Filter change handlers
    gameTypeSel?.addEventListener('change', function () {
        serverSel.value = '';
        rebuildServerOptions();
        table.page('first').draw(false);
    });

    serverSel?.addEventListener('change', function () {
        table.page('first').draw(false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        if (gameTypeSel) gameTypeSel.value = '';
        if (serverSel) serverSel.value = '';
        rebuildServerOptions();
        table.search('');
        table.page('first').draw(false);
    });
});
