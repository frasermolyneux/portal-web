// Game Servers Index - Initialize DataTables with responsive
$(document).ready(function () {
    const gameServersTable = $('#gameServersTable');

    if (gameServersTable.length && gameServersTable.find('tbody tr').length > 0) {
        const table = gameServersTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[0, 'asc']], // Position ascending
            columnDefs: [
                { targets: 0, responsivePriority: 3 }, // Game Type
                { targets: 1, responsivePriority: 1 }, // Title - always visible
                { targets: 2, responsivePriority: 4 }, // Hostname
                { targets: 3, responsivePriority: 7 }, // FileTransportEnabled
                { targets: 4, responsivePriority: 8 }, // RconEnabled
                { targets: 5, responsivePriority: 9 }, // AgentEnabled
                { targets: 6, responsivePriority: 10 }, // BanFileSyncEnabled
                { targets: 7, responsivePriority: 11 }, // ServerListEnabled
                { targets: 8, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No game servers found'
            }
        });

        function relocateSearch() {
            try {
                if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
                    return;
                }

                window.PortalDataTableUi.relocateSearch({
                    filtersContainerId: 'gameServersFilters',
                    dataTableFilterId: 'gameServersTable_filter',
                    placeholder: 'Search servers...',
                    inputId: 'globalGameServersSearch'
                });
            } catch { /* swallow */ }
        }

        table.on('init.dt', function () { relocateSearch(); });
        setTimeout(relocateSearch, 1000);

        document.getElementById('resetFilters')?.addEventListener('click', function () {
            if (table.search()) {
                table.search('').draw();
            }
        });

        const iboxContent = gameServersTable.closest('.ibox-content');
        if (iboxContent.length && iboxContent[0] && iboxContent[0].classList) {
            iboxContent[0].classList.add('datatable-tight');
        }
    }
});
