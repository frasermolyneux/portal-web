// Unclaimed Bans page script
$(document).ready(function () {
    const table = $('#dataTable').DataTable({
        processing: true,
        serverSide: true,
        searching: false,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[0, 'desc']],
        columnDefs: [
            { targets: 0, responsivePriority: 5, orderable: true }, // Created
            { targets: 1, responsivePriority: 2, orderable: false }, // Type
            { targets: 2, responsivePriority: 1, orderable: false }, // Player (with game icon)
            { targets: 3, responsivePriority: 4, orderable: false }, // Expires
            { targets: 4, orderable: false, responsivePriority: 3 } // Action
        ],
        ajax: {
            url: '/AdminActions/GetUnclaimedAdminActionsAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                xhr.setRequestHeader('RequestVerificationToken', token);
                const gt = $('#filterGameType').val();
                const baseUrl = '/AdminActions/GetUnclaimedAdminActionsAjax';
                let qs = [];
                if (gt) qs.push('gameType=' + encodeURIComponent(gt));
                this.url = baseUrl + (qs.length ? ('?' + qs.join('&')) : '');
            }
        },
        columns: [
            { data: 'created', name: 'created', orderable: true, render: function (data) { return '<span title="' + data + '">' + timeAgo(data) + '</span>'; } },
            { data: 'type', name: 'type', orderable: false, render: function (data) { return adminActionTypeIcon(data); } },
            { data: 'player', name: 'player', orderable: false, render: function (data, type, row) { 
                return renderPlayerName(row.gameType, data, row.playerId) + '<br/><small class="text-muted">' + (row.guid || '') + '</small>'; 
            } },
            { data: 'expires', name: 'expires', orderable: false, className: 'expires-cell', render: function (data) { return formatExpiryDate(data); } },
            {
                data: null, name: 'action', orderable: false, className: 'text-center', render: function (data, type, row) {
                    if (!row.canClaim) return '';
                    return '<a href="/AdminActions/Claim/' + row.adminActionId + '" class="btn btn-outline-info btn-sm claim-btn"><i class="fa-solid fa-user me-1"></i> Claim Ban</a>';
                }
            }
        ]
    });

    table.on('xhr.dt', function () { table.columns.adjust(); });
    $('#dataTable').on('init.dt', function () { setTimeout(function () { table.columns.adjust().draw(false); }, 250); });

    function applyGameFilterVisibility() {
        // Game column removed - no longer needed
    }
    applyGameFilterVisibility();

    $('#filterGameType').on('change', function () {
        applyGameFilterVisibility();
        table.ajax.reload(null, false);
    });

    $('#resetFilters').on('click', function () {
        const changed = $('#filterGameType').val() !== '';
        $('#filterGameType').val('');
        applyGameFilterVisibility();
        if (changed) {
            table.page('first').draw('page');
        } else {
            table.ajax.reload(null, false);
        }
    });
    // No manual resize handler – rely fully on DataTables Responsive
});
