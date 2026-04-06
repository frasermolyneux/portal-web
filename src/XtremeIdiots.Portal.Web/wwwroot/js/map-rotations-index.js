$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[1, 'asc']],
        columnDefs: [
            { targets: 0, responsivePriority: 2 }, // Game
            { targets: 1, responsivePriority: 1 }, // Title
            { targets: 2, responsivePriority: 3 }, // Game Mode
            { targets: 3, responsivePriority: 4 }, // Maps
            { targets: 4, responsivePriority: 5 }, // Servers
            { targets: 5, responsivePriority: 1, orderable: false }  // Actions
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) {
                    xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                }
                const gt = document.getElementById('filterGameType')?.value;
                let base = dataUrl;
                if (gt) {
                    base = dataUrl.replace(/\/MapRotations\/GetMapRotationsAjax.*/, '/MapRotations/GetMapRotationsAjax/' + encodeURIComponent(gt));
                }
                this.url = base;
            }
        },
        columns: [
            { data: 'gameType', name: 'gameType', orderable: true, render: function (data) { return gameTypeIcon(data); } },
            { data: 'title', name: 'title', orderable: true },
            { data: 'gameMode', name: 'gameMode', orderable: false },
            { data: 'mapCount', name: 'mapCount', orderable: false, render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; } },
            { data: 'serverCount', name: 'serverCount', orderable: false, render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; } },
            {
                data: null, name: 'actions', orderable: false, render: function (data, type, row) {
                    var html = '<div class="btn-group btn-group-sm" role="group">';
                    html += '<a href="/MapRotations/Details/' + row.mapRotationId + '" class="btn btn-outline-primary btn-xs"><i class="fa-solid fa-eye"></i> Details</a>';
                    html += '<a href="/MapRotations/Edit/' + row.mapRotationId + '" class="btn btn-outline-secondary btn-xs"><i class="fa-solid fa-edit"></i> Edit</a>';
                    html += '</div>';
                    return html;
                }
            }
        ]
    });

    function applyGameColumnVisibility() {
        const hasSpecificGame = document.getElementById('filterGameType')?.value !== '';
        table.column(0).visible(!hasSpecificGame, false);
    }

    document.getElementById('filterGameType')?.addEventListener('change', function () {
        applyGameColumnVisibility();
        table.ajax.reload(null, false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        const sel = document.getElementById('filterGameType');
        let changed = false;
        if (sel && sel.value !== '') {
            sel.value = '';
            applyGameColumnVisibility();
            changed = true;
        }
        if (table.search()) {
            table.search('');
            changed = true;
        }
        if (changed) {
            table.page('first');
        }
        table.draw(false);
    });

    applyGameColumnVisibility();

    const iboxContent = tableEl.closest('.ibox-content');
    if (iboxContent) {
        iboxContent.classList.add('datatable-tight');
    }
});
