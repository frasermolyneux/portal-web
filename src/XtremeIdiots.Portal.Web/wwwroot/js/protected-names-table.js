// Protected Names tables - Initialize DataTables with responsive
$(document).ready(function () {
    const protectedNamesTable = $('#protectedNamesTable');

    if (protectedNamesTable.length && protectedNamesTable.find('tbody tr').length > 0) {
        const table = protectedNamesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[3, 'desc']], // Created date desc (column index 3)
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 3 }, // Owner/Player - medium priority
                { targets: 2, responsivePriority: 4 }, // Owner game - lower priority
                { targets: 3, responsivePriority: 5 }, // Created - lower priority
                { targets: 4, responsivePriority: 6 }, // Created By - lowest priority
                { targets: 5, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No protected names found'
            }
        });

        function relocateSearch() {
            try {
                if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
                    return;
                }

                window.PortalDataTableUi.relocateSearch({
                    filtersContainerId: 'protectedNamesFilters',
                    dataTableFilterId: 'protectedNamesTable_filter',
                    placeholder: 'Search protected names...',
                    inputId: 'globalProtectedNamesSearch'
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

        const iboxContent = protectedNamesTable.closest('.ibox-content');
        if (iboxContent.length && iboxContent[0] && iboxContent[0].classList) {
            iboxContent[0].classList.add('datatable-tight');
        }
    }
});
