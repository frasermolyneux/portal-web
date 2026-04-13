// Permissions Report page script
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const gameTypeSel = document.getElementById('filterGameType');
    const claimTypeSel = document.getElementById('filterClaimType');

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[0, 'asc']],
        stateSaveParams: function (settings, data) {
            data._permissionsReportStructureVersion = 1;
            if (gameTypeSel) data.gameType = gameTypeSel.value || '';
            if (claimTypeSel) data.claimType = claimTypeSel.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._permissionsReportStructureVersion !== 1) return false;
            if (gameTypeSel && typeof data.gameType !== 'undefined') gameTypeSel.value = data.gameType;
            if (claimTypeSel && typeof data.claimType !== 'undefined') claimTypeSel.value = data.claimType;
        },
        columnDefs: [
            { targets: 0, responsivePriority: 1 },
            { targets: 1, responsivePriority: 2 },
            { targets: 2, responsivePriority: 3 },
            { targets: 3, responsivePriority: 4 },
            { targets: 4, responsivePriority: 5, orderable: false }
        ],
        ajax: {
            url: '/User/GetPermissionsReportAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);

                var baseUrl = '/User/GetPermissionsReportAjax';
                var params = [];
                var gtVal = gameTypeSel?.value;
                var ctVal = claimTypeSel?.value;
                if (gtVal) params.push('gameType=' + encodeURIComponent(gtVal));
                if (ctVal) params.push('claimType=' + encodeURIComponent(ctVal));
                this.url = params.length > 0 ? baseUrl + '?' + params.join('&') : baseUrl;
            }
        },
        columns: [
            {
                data: 'displayName', name: 'displayName', orderable: false,
                render: function (data, type, row) {
                    return '<a href="/User/ManageProfile/' + row.userProfileId + '">' + escapeHtml(data || '—') + '</a>';
                }
            },
            {
                data: 'claimTypeDisplayName', name: 'claimType', orderable: false,
                render: function (data, type, row) {
                    var html = escapeHtml(data);
                    if (data !== row.claimType) {
                        html += '<br><small class="text-muted"><code>' + escapeHtml(row.claimType) + '</code></small>';
                    }
                    return html;
                }
            },
            {
                data: 'claimValue', name: 'claimValue', orderable: false,
                render: function (data) {
                    return '<code>' + escapeHtml(data || '') + '</code>';
                }
            },
            {
                data: 'systemGenerated', name: 'systemGenerated', orderable: false,
                render: function (data) {
                    return data
                        ? '<span class="badge bg-info">System</span>'
                        : '<span class="badge bg-secondary">Manual</span>';
                }
            },
            {
                data: null, orderable: false, defaultContent: '',
                render: function (data, type, row) {
                    return '<a href="/User/ManageProfile/' + row.userProfileId + '" class="btn btn-outline-secondary btn-sm" title="Manage Profile"><i class="fa-solid fa-fw fa-pen-to-square" aria-hidden="true"></i></a>';
                }
            }
        ],
        initComplete: function () {
            relocateSearch();
        }
    });

    function relocateSearch() {
        try {
            var filters = document.getElementById('permissionsReportFilters');
            var dtFilter = document.getElementById('dataTable_filter');
            if (!filters || !dtFilter) return;
            if (dtFilter.classList) dtFilter.classList.add('filter-group');
            var label = dtFilter.querySelector('label');
            if (label) {
                var input = label.querySelector('input');
                if (input) {
                    if (input.classList) input.classList.add('form-control');
                    input.placeholder = 'Search...';
                    label.textContent = '';
                    var newLabel = document.createElement('label');
                    newLabel.className = 'form-label';
                    newLabel.setAttribute('for', input.id || 'globalPermissionsSearch');
                    if (!input.id) input.id = 'globalPermissionsSearch';
                    newLabel.textContent = 'Search';
                    dtFilter.appendChild(newLabel);
                    dtFilter.appendChild(input);
                }
            }
            var resetBtn = document.getElementById('resetFilters');
            var resetGroup = resetBtn ? resetBtn.closest('.filter-group') : null;
            if (resetGroup && resetGroup.parentElement === filters) {
                filters.insertBefore(dtFilter, resetGroup);
            } else {
                filters.appendChild(dtFilter);
            }
        } catch (e) { /* swallow */ }
    }

    function reloadTable() { table.ajax.reload(null, false); }

    gameTypeSel?.addEventListener('change', reloadTable);
    claimTypeSel?.addEventListener('change', reloadTable);

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        var changed = false;
        if (gameTypeSel && gameTypeSel.value !== '') { gameTypeSel.value = ''; changed = true; }
        if (claimTypeSel && claimTypeSel.value !== '') { claimTypeSel.value = ''; changed = true; }
        if (table.search()) { table.search(''); changed = true; }
        if (changed) table.page('first');
        table.draw(false);
    });

    var iboxContent = tableEl.closest('.ibox-content')[0];
    if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
});
