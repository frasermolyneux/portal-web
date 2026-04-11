// Maps Vote Log page script
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searching: false,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[5, 'desc']],
        columnDefs: [
            { targets: 0, responsivePriority: 3 }, // Game
            { targets: 1, responsivePriority: 1 }, // Map
            { targets: 2, responsivePriority: 2 }, // Player
            { targets: 3, responsivePriority: 5 }, // Server
            { targets: 4, responsivePriority: 4 }, // Vote
            { targets: 5, responsivePriority: 3 }  // Date
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) {
                    xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                }
                var gt = document.getElementById('filterGameType')?.value;
                var url = dataUrl;
                if (gt) {
                    url += '?gameType=' + encodeURIComponent(gt);
                }
                this.url = url;
            }
        },
        columns: [
            {
                data: 'gameType', name: 'gameType', orderable: false, render: function (data) {
                    if (!data) return '<span class="text-muted">Unknown</span>';
                    return gameTypeIcon(data);
                }
            },
            {
                data: 'mapName', name: 'mapName', orderable: true, render: function (data) {
                    if (!data) return '<span class="text-muted">Unknown</span>';
                    return escapeHtml(data);
                }
            },
            {
                data: null, name: 'playerName', orderable: false, render: function (data, type, row) {
                    if (!row.playerName) return '<span class="text-muted">Unknown</span>';
                    var coloredName = CodColors.renderSafe(row.playerName);
                    if (row.playerId) {
                        return '<a href="/Players/Details/' + row.playerId + '">' + coloredName + '</a>';
                    }
                    return coloredName;
                }
            },
            {
                data: 'serverName', name: 'serverName', orderable: false, render: function (data) {
                    if (!data) return '<span class="text-muted">N/A</span>';
                    return CodColors.renderSafe(data);
                }
            },
            {
                data: 'like', name: 'like', orderable: false, render: function (data) {
                    if (data) {
                        return '<span class="badge bg-info"><i class="fa fa-thumbs-up"></i> Like</span>';
                    }
                    return '<span class="badge bg-danger"><i class="fa fa-thumbs-down"></i> Dislike</span>';
                }
            },
            { data: 'timestamp', name: 'timestamp', orderable: true }
        ]
    });

    function applyGameColumnVisibility() {
        var hasSpecificGame = document.getElementById('filterGameType')?.value !== '';
        table.column(0).visible(!hasSpecificGame, false);
    }

    document.getElementById('filterGameType')?.addEventListener('change', function () {
        applyGameColumnVisibility();
        table.ajax.reload(null, false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        var sel = document.getElementById('filterGameType');
        var changed = false;
        if (sel && sel.value !== '') {
            sel.value = '';
            applyGameColumnVisibility();
            changed = true;
        }
        if (changed) {
            table.page('first');
        }
        table.draw(false);
    });

    applyGameColumnVisibility();

    // Reduce internal padding under the table region
    var iboxContent = tableEl.closest('.ibox-content');
    if (iboxContent) {
        iboxContent.classList.add('datatable-tight');
    }
});
