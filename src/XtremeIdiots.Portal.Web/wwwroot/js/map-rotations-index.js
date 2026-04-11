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
            { targets: 0, responsivePriority: 3, width: '40px' },
            { targets: 1, responsivePriority: 1 },
            { targets: 2, responsivePriority: 4, width: '70px' },
            { targets: 3, responsivePriority: 5, width: '80px' },
            { targets: 4, responsivePriority: 6, width: '50px' },
            { targets: 5, responsivePriority: 7, width: '50px' },
            { targets: 6, responsivePriority: 8, width: '100px' },
            { targets: 7, responsivePriority: 10, visible: false },
            { targets: 8, responsivePriority: 2, orderable: false, width: '120px' }
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);

                var gt = document.getElementById('filterGameType')?.value;
                var base = dataUrl;
                if (gt) base = dataUrl.replace(/\/MapRotations\/GetMapRotationsAjax.*/, '/MapRotations/GetMapRotationsAjax/' + encodeURIComponent(gt));
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
                    if (row.category) html += ' <span class="badge bg-outline-secondary small">' + escapeHtml(row.category) + '</span>';
                    if (row.description) {
                        var desc = row.description.length > 60 ? row.description.substring(0, 60) + '…' : row.description;
                        html += '<br><small class="text-muted">' + escapeHtml(desc) + '</small>';
                    }
                    return html;
                }
            },
            {
                data: 'gameMode', name: 'gameMode', orderable: true,
                render: function (data) { return '<span class="badge bg-info">' + escapeHtml(data) + '</span>'; }
            },
            {
                data: 'status', name: 'status', orderable: false,
                render: function (data) { return statusBadgeMap[data] || '<span class="badge bg-secondary">' + escapeHtml(data) + '</span>'; }
            },
            {
                data: 'mapCount', name: 'mapCount', orderable: true,
                render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; }
            },
            {
                data: 'serverCount', name: 'serverCount', orderable: true,
                render: function (data) { return '<span class="badge bg-secondary">' + data + '</span>'; }
            },
            {
                data: 'updatedAt', name: 'updatedAt', orderable: true,
                render: function (data, type, row) {
                    var html = '<small>' + escapeHtml(data) + '</small>';
                    var author = row.lastModifiedByDisplayName || row.createdByDisplayName;
                    if (author) html += '<br><small class="text-muted"><i class="fa-solid fa-user fa-xs me-1"></i>' + escapeHtml(author) + '</small>';
                    return html;
                }
            },
            {
                data: 'createdByDisplayName', name: 'createdBy', orderable: true, visible: false
            },
            {
                data: null, name: 'actions', orderable: false,
                render: function (data, type, row) {
                    var html = '<div class="btn-group btn-group-sm" role="group">';
                    html += '<a href="/MapRotations/Details/' + row.mapRotationId + '" class="btn btn-outline-primary btn-xs" title="Details"><i class="fa-solid fa-eye"></i></a>';
                    html += '<a href="/MapRotations/Edit/' + row.mapRotationId + '" class="btn btn-outline-primary btn-xs" title="Edit"><i class="fa-solid fa-edit"></i></a>';
                    html += '<a href="/MapRotations/Clone/' + row.mapRotationId + '" class="btn btn-outline-primary btn-xs" title="Clone"><i class="fa-solid fa-clone"></i></a>';
                    html += '<button type="button" class="btn btn-outline-danger btn-xs btn-delete" data-id="' + row.mapRotationId + '" data-title="' + escapeHtml(row.title) + '" title="Delete"><i class="fa-solid fa-trash"></i></button>';
                    html += '</div>';
                    return html;
                }
            }
        ],
        initComplete: function () {
            relocateSearch();
        }
    });

    function relocateSearch() {
        try {
            var dtFilter = document.getElementById('dataTable_filter');
            var placeholder = document.getElementById('searchFilterGroup');
            if (!dtFilter || !placeholder) return;

            var label = dtFilter.querySelector('label');
            if (label) {
                var input = label.querySelector('input');
                if (input) {
                    input.classList.add('form-control', 'form-control-sm');
                    input.classList.remove('form-control-sm');
                    input.setAttribute('placeholder', 'Search rotations...');

                    // Rebuild the filter group to match other filters
                    var newGroup = document.createElement('div');
                    newGroup.className = 'filter-group';
                    var newLabel = document.createElement('label');
                    newLabel.className = 'form-label';
                    newLabel.textContent = 'Search';
                    newGroup.appendChild(newLabel);
                    newGroup.appendChild(input);

                    var resetGroup = document.getElementById('resetFilters')?.closest('.filter-group');
                    if (resetGroup && resetGroup.parentElement) {
                        resetGroup.parentElement.insertBefore(newGroup, resetGroup);
                    }
                }
            }
            dtFilter.style.display = 'none';
            placeholder.remove();
        } catch (e) { /* swallow */ }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function applyGameColumnVisibility() {
        var hasSpecificGame = document.getElementById('filterGameType')?.value !== '';
        table.column(0).visible(!hasSpecificGame, false);
    }

    // Server-side filters
    document.getElementById('filterGameType')?.addEventListener('change', function () {
        applyGameColumnVisibility();
        table.ajax.reload(null, false);
    });

    document.getElementById('filterGameMode')?.addEventListener('change', function () {
        table.column('gameMode:name').search(this.value).draw();
    });

    document.getElementById('filterStatus')?.addEventListener('change', function () {
        table.column('status:name').search(this.value).draw();
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        ['filterGameType', 'filterGameMode', 'filterStatus'].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) el.value = '';
        });
        var myBtn = document.getElementById('filterMyRotations');
        if (myBtn) {
            myBtn.classList.remove('active', 'btn-primary');
            myBtn.classList.add('btn-outline-primary');
        }
        applyGameColumnVisibility();
        table.columns().search('');
        table.search('');
        table.page('first').draw(false);
    });

    // "My Rotations" toggle
    var myRotationsBtn = document.getElementById('filterMyRotations');
    if (myRotationsBtn) {
        myRotationsBtn.addEventListener('click', function () {
            var isActive = this.classList.contains('active');
            if (isActive) {
                this.classList.remove('active', 'btn-primary');
                this.classList.add('btn-outline-primary');
                table.column('createdBy:name').search('').draw();
            } else {
                this.classList.add('active', 'btn-primary');
                this.classList.remove('btn-outline-primary');
                var userId = this.dataset.userProfileId;
                if (userId) {
                    table.column('createdBy:name').search(userId).draw();
                }
            }
        });
    }

    // Delete handler
    tableEl.on('click', '.btn-delete', function () {
        var id = $(this).data('id');
        var title = $(this).data('title');
        if (!confirm('Are you sure you want to delete "' + title + '"?')) return;

        var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        $.ajax({
            url: '/MapRotations/Delete/' + id,
            type: 'POST',
            headers: { 'RequestVerificationToken': token },
            success: function () { table.ajax.reload(null, false); },
            error: function () { alert('Failed to delete rotation.'); }
        });
    });

    applyGameColumnVisibility();

    var iboxContent = tableEl.closest('.ibox-content');
    if (iboxContent) iboxContent.classList.add('datatable-tight');
});
