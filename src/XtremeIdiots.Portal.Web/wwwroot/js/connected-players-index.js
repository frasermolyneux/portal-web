$(document).ready(function () {
    const tableEl = $('#dataTable');
    const gameSel = document.getElementById('gameType');
    const statusSel = document.getElementById('isActive');

    if (!tableEl.length) {
        return;
    }

    const table = tableEl.DataTable({
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
        applyFilters();
        table.columns.adjust().responsive.recalc();
    });

    setTimeout(function () {
        relocateSearch();
        if (table.responsive) {
            table.columns.adjust().responsive.recalc();
        }
    }, 1000);

    function applyFilters() {
        const game = gameSel?.value || '';
        const status = statusSel?.value || '';

        table.column(0).search(game ? '^' + game + '$' : '', true, false);
        table.column(5).search(status ? '^' + status + '$' : '', true, false);
        table.draw(false);
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
