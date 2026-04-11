$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');

    const statusBadgeMap = {
        'Draft': '<span class="badge bg-warning text-dark">Draft</span>',
        'Testing': '<span class="badge bg-primary">Testing</span>',
        'Active': '<span class="badge bg-success">Active</span>',
        'Archived': '<span class="badge bg-dark">Archived</span>'
    };

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[1, 'asc']],
        columnDefs: [
            { targets: 0, responsivePriority: 3, width: '40px' },   // Game
            { targets: 1, responsivePriority: 1 },                   // Title
            { targets: 2, responsivePriority: 4, width: '70px' },    // Mode
            { targets: 3, responsivePriority: 5, width: '80px' },    // Status
            { targets: 4, responsivePriority: 6, width: '60px' },    // Maps
            { targets: 5, responsivePriority: 7, width: '60px' },    // Servers
            { targets: 6, responsivePriority: 8, width: '90px' },    // Updated
            { targets: 7, responsivePriority: 2, orderable: false, width: '200px' } // Actions
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
            {
                data: 'gameType', name: 'gameType', orderable: true,
                render: function (data) { return gameTypeIcon(data); }
            },
            {
                data: 'title', name: 'title', orderable: true,
                render: function (data, type, row) {
                    var html = '<strong>' + escapeHtml(data) + '</strong>';
                    if (row.category) {
                        html += ' <span class="badge bg-outline-secondary small">' + escapeHtml(row.category) + '</span>';
                    }
                    if (row.description) {
                        var desc = row.description.length > 80 ? row.description.substring(0, 80) + '…' : row.description;
                        html += '<br><small class="text-muted">' + escapeHtml(desc) + '</small>';
                    }
                    return html;
                }
            },
            {
                data: 'gameMode', name: 'gameMode', orderable: false,
                render: function (data) { return '<span class="badge bg-info">' + escapeHtml(data) + '</span>'; }
            },
            {
                data: 'status', name: 'status', orderable: false,
                render: function (data) { return statusBadgeMap[data] || '<span class="badge bg-secondary">' + escapeHtml(data) + '</span>'; }
            },
            {
                data: 'mapCount', name: 'mapCount', orderable: false,
                render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; }
            },
            {
                data: 'serverCount', name: 'serverCount', orderable: false,
                render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; }
            },
            {
                data: 'updatedAt', name: 'updatedAt', orderable: false,
                render: function (data) { return '<small class="text-muted">' + escapeHtml(data) + '</small>'; }
            },
            {
                data: null, name: 'actions', orderable: false,
                render: function (data, type, row) {
                    var html = '<div class="btn-group btn-group-sm" role="group">';
                    html += '<a href="/MapRotations/Details/' + row.mapRotationId + '" class="btn btn-outline-primary btn-xs" title="Details"><i class="fa-solid fa-eye"></i></a>';
                    html += '<a href="/MapRotations/Edit/' + row.mapRotationId + '" class="btn btn-outline-secondary btn-xs" title="Edit"><i class="fa-solid fa-edit"></i></a>';
                    html += '<a href="/MapRotations/Clone/' + row.mapRotationId + '" class="btn btn-outline-secondary btn-xs" title="Clone"><i class="fa-solid fa-clone"></i></a>';
                    html += '</div>';
                    return html;
                }
            }
        ],
        initComplete: function () {
            // Relocate search to filter bar (matching Players index pattern)
            var searchInput = tableEl.closest('.dataTables_wrapper').find('.dataTables_filter input');
            searchInput.addClass('form-control form-control-sm');
            searchInput.attr('placeholder', 'Search rotations...');
            searchInput.detach().appendTo('#searchFilterGroup');
            tableEl.closest('.dataTables_wrapper').find('.dataTables_filter').hide();
        }
    });

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function applyGameColumnVisibility() {
        const hasSpecificGame = document.getElementById('filterGameType')?.value !== '';
        table.column(0).visible(!hasSpecificGame, false);
    }

    document.getElementById('filterGameType')?.addEventListener('change', function () {
        applyGameColumnVisibility();
        table.ajax.reload(null, false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        var changed = false;
        var sel = document.getElementById('filterGameType');
        if (sel && sel.value !== '') { sel.value = ''; changed = true; }
        applyGameColumnVisibility();
        if (table.search()) { table.search(''); changed = true; }
        if (changed) { table.page('first'); }
        table.draw(false);
    });

    applyGameColumnVisibility();

    const iboxContent = tableEl.closest('.ibox-content');
    if (iboxContent) {
        iboxContent.classList.add('datatable-tight');
    }
});
