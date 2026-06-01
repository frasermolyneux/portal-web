$(document).ready(function () {
    const tableEl = $('#dataTable');
    const gameSel = document.getElementById('gameType');
    const statusSel = document.getElementById('isActive');
    const canUnlink = tableEl.data('can-unlink') === true || tableEl.data('can-unlink') === 'true';

    if (!tableEl.length) {
        return;
    }

    function htmlEncode(value) {
        return $('<div/>').text(value ?? '').html();
    }

    function renderUserProfileLink(userProfileId) {
        const encoded = htmlEncode(userProfileId);
        return '<a href="/User/ManageProfile/' + encoded + '">' + encoded + '</a>';
    }

    function renderStatusBadge(isActive) {
        return isActive
            ? '<span class="badge bg-success">Active</span>'
            : '<span class="badge bg-secondary">Inactive</span>';
    }

    function renderTimeCell(utcDate) {
        if (!utcDate) {
            return '';
        }

        const formatted = portalDate.formatDateTime(utcDate);
        return '<span title="' + htmlEncode(formatted) + '">' + htmlEncode(formatted) + '</span>';
    }

    function renderUnlinkAction(row) {
        if (!canUnlink || !row.isActive) {
            return '';
        }

        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';
        const id = htmlEncode(row.connectedPlayerProfileId);

        return '' +
            '<form action="/ConnectedPlayers/ForceUnlink" method="post" class="d-inline">' +
            '<input name="__RequestVerificationToken" type="hidden" value="' + htmlEncode(token) + '" />' +
            '<input type="hidden" name="connectedPlayerProfileId" value="' + id + '" />' +
            '<button type="submit" class="btn btn-outline-danger btn-sm" data-confirm="Are you sure you want to unlink this connected player?">' +
            '<i class="fa-solid fa-fw fa-link-slash" aria-hidden="true"></i> Unlink' +
            '</button>' +
            '</form>';
    }

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        stateSave: true,
        stateSaveParams: function (settings, data) {
            data._connectedPlayersStructureVersion = 1;
            data.filterGameType = gameSel?.value || '';
            data.filterStatus = statusSel?.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._connectedPlayersStructureVersion !== 1) {
                return false;
            }

            if (gameSel && typeof data.filterGameType !== 'undefined') {
                gameSel.value = data.filterGameType;
            }

            if (statusSel && typeof data.filterStatus !== 'undefined') {
                statusSel.value = data.filterStatus;
            }
        },
        pageLength: 25,
        order: [[6, 'desc']],
        ajax: {
            url: '/ConnectedPlayers/GetConnectedPlayersAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) {
                    xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                }

                const url = new URL('/ConnectedPlayers/GetConnectedPlayersAjax', window.location.origin);
                if (gameSel?.value) {
                    url.searchParams.set('gameType', gameSel.value);
                }

                if (statusSel?.value) {
                    url.searchParams.set('isActive', statusSel.value === 'Active' ? 'true' : 'false');
                }

                this.url = url.pathname + url.search;
            }
        },
        columnDefs: [
            { targets: 0, responsivePriority: 3 },
            { targets: 1, responsivePriority: 1 },
            { targets: 2, responsivePriority: 5 },
            { targets: 3, responsivePriority: 4 },
            { targets: 4, responsivePriority: 8 },
            { targets: 5, responsivePriority: 2 },
            { targets: 6, responsivePriority: 6 },
            { targets: 7, responsivePriority: 9 },
            { targets: 8, responsivePriority: 7, orderable: false, searchable: false }
        ],
        columns: [
            { data: 'gameType', name: 'gameType', orderable: true },
            { data: 'username', name: 'username', orderable: true, render: function (data) { return htmlEncode(data); } },
            { data: 'playerId', name: 'playerId', orderable: false, render: function (data) { return '<code>' + htmlEncode(data) + '</code>'; } },
            { data: 'userProfileId', name: 'userProfileId', orderable: false, render: function (data) { return renderUserProfileLink(data); } },
            { data: 'linkMethod', name: 'linkMethod', orderable: true, render: function (data) { return htmlEncode(data); } },
            { data: 'isActive', name: 'isActive', orderable: true, render: function (data, type, row) { return renderStatusBadge(row.isActive); } },
            { data: 'linkedAtUtc', name: 'linkedAtUtc', orderable: true, render: function (data) { return renderTimeCell(data); } },
            { data: 'unlinkedAtUtc', name: 'unlinkedAtUtc', orderable: true, render: function (data) { return renderTimeCell(data); } },
            { data: null, defaultContent: '', orderable: false, searchable: false, render: function (data, type, row) { return renderUnlinkAction(row); } }
        ],
        language: {
            emptyTable: 'No connected player links found.'
        }
    });

    function relocateSearch() {
        try {
            const filters = document.getElementById('connectedPlayersFilters');
            const dtFilter = document.getElementById('dataTable_filter');
            if (!filters || !dtFilter) {
                return;
            }

            dtFilter.classList.add('filter-group');

            const label = dtFilter.querySelector('label');
            if (label) {
                const input = label.querySelector('input');
                if (input) {
                    input.classList.add('form-control');
                    input.placeholder = 'Search connected players...';

                    label.textContent = '';
                    const newLabel = document.createElement('label');
                    newLabel.className = 'form-label';
                    newLabel.setAttribute('for', input.id || 'globalConnectedPlayersSearch');
                    if (!input.id) {
                        input.id = 'globalConnectedPlayersSearch';
                    }
                    newLabel.textContent = 'Search';
                    dtFilter.appendChild(newLabel);
                    dtFilter.appendChild(input);
                }
            }

            const resetBtn = document.getElementById('resetFilters');
            const resetGroup = resetBtn ? resetBtn.closest('.filter-group') : null;
            if (resetGroup && resetGroup.parentElement === filters) {
                filters.insertBefore(dtFilter, resetGroup);
            } else {
                filters.appendChild(dtFilter);
            }
        } catch {
            // Intentionally no-op; table remains functional if search relocation fails.
        }
    }

    table.on('init.dt', function () {
        relocateSearch();
        table.columns.adjust().responsive.recalc();
    });

    setTimeout(function () {
        relocateSearch();
        if (table.responsive) {
            table.columns.adjust().responsive.recalc();
        }
    }, 1000);

    function applyFilters() {
        table.ajax.reload(null, false);
    }

    gameSel?.addEventListener('change', applyFilters);
    statusSel?.addEventListener('change', applyFilters);

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        let changed = false;

        if (gameSel && gameSel.value !== '') {
            gameSel.value = '';
            changed = true;
        }

        if (statusSel && statusSel.value !== '') {
            statusSel.value = '';
            changed = true;
        }

        if (table.search()) {
            table.search('');
            changed = true;
        }

        if (changed) {
            table.column(0).search('', true, false);
            table.column(5).search('', true, false);
            table.page('first');
            table.draw(false);
        }
    });

    const iboxContent = tableEl.closest('.ibox-content')[0];
    if (iboxContent && iboxContent.classList) {
        iboxContent.classList.add('datatable-tight');
    }
});
