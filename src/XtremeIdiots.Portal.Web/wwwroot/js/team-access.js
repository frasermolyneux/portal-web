$(function () {
    const tableElement = $('#teamAccessTable');
    const gameType = tableElement.data('game-type');
    const gameTypeSelect = document.getElementById('filterGameType');
    const teamAccessUrlTemplate = tableElement.data('team-access-url-template') || '/User/TeamAccess?gameType=__GAME_TYPE__';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    const table = tableElement.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[0, 'asc']],
        stateSaveParams: function (settings, data) {
            data._teamAccessStructureVersion = 1;
            data.gameType = gameType;
        },
        stateLoadParams: function (settings, data) {
            return data._teamAccessStructureVersion === 1 && data.gameType === gameType;
        },
        columnDefs: [
            { targets: 0, responsivePriority: 1 },
            { targets: 1, responsivePriority: 3 },
            { targets: 2, responsivePriority: 4 },
            { targets: 3, responsivePriority: 2 }
        ],
        ajax: {
            url: '/User/GetGameModeratorsAjax?gameType=' + encodeURIComponent(gameType),
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: data => JSON.stringify(data),
            beforeSend: xhr => {
                if (token) xhr.setRequestHeader('RequestVerificationToken', token);
            }
        },
        columns: [
            {
                data: 'displayName', name: 'displayName',
                render: (data, type, row) => '<a href="/User/ManageProfile/' + encodeURIComponent(row.userProfileId) + '">' + $('<div>').text(data || '—').html() + '</a>'
            },
            {
                data: 'claims', orderable: false,
                render: claims => {
                    const safeClaims = Array.isArray(claims) ? claims : [];
                    return safeClaims.some(claim => claim.systemGenerated && claim.claimType === 'Moderator')
                        ? '<span class="badge bg-info">Inherited Moderator role</span>' : 'Moderator';
                }
            },
            {
                data: 'claims', orderable: false,
                render: claims => {
                    const safeClaims = Array.isArray(claims) ? claims : [];
                    return safeClaims.filter(claim => !claim.systemGenerated)
                        .map(claim => {
                            const claimType = claim.claimTypeDisplayName ?? claim.claimType ?? 'Unknown permission';
                            const claimValue = claim.claimValueDisplayName ?? claim.claimValue ?? '—';
                            return $('<span>').text(claimType).html() + ' (' + $('<span>').text(String(claimValue)).html() + ')';
                        })
                        .join('<br>') || 'None';
                }
            },
            {
                data: 'userProfileId', orderable: false,
                render: id => '<a class="btn btn-outline-secondary btn-sm" href="/User/ManageProfile/' + encodeURIComponent(id) + '?tab=permissions#permissions"><i class="fa-solid fa-fw fa-pen-to-square" aria-hidden="true"></i> Manage Profile</a>'
            }
        ],
        language: {
            emptyTable: 'No moderators found for this game'
        }
    });

    function relocateSearch() {
        if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
            return;
        }

        window.PortalDataTableUi.relocateSearch({
            filtersContainerId: 'teamAccessFilters',
            dataTableFilterId: 'teamAccessTable_filter',
            placeholder: 'Search moderators...',
            inputId: 'teamAccessSearch'
        });
    }

    table.on('init.dt', function () {
        relocateSearch();
        if (window.PortalDataTableUi && typeof window.PortalDataTableUi.attachPageJump === 'function') {
            window.PortalDataTableUi.attachPageJump(table, { label: 'Page' });
        }
    });
    setTimeout(relocateSearch, 1000);

    gameTypeSelect?.addEventListener('change', function () {
        const targetUrl = String(teamAccessUrlTemplate).replace('__GAME_TYPE__', encodeURIComponent(this.value));
        window.location.assign(targetUrl);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        if (table.search()) {
            table.search('');
        }
        table.page('first').draw(false);
    });

    tableElement.closest('.ibox-content').addClass('datatable-tight');
});
