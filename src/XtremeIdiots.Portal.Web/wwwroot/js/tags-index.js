// Tags Index - Initialize DataTables with responsive
$(document).ready(function () {
    const tagsTable = $('#tagsTable');

    if (tagsTable.length && tagsTable.find('tbody tr').length > 0) {
        const table = tagsTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[0, 'asc']], // Name ascending
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 4 }, // Description - lower priority
                { targets: 2, responsivePriority: 3 }, // Tag Preview - medium priority
                { targets: 3, responsivePriority: 5 }, // Players count - lowest priority
                { targets: 4, responsivePriority: 6 }, // User Defined - very low priority
                { targets: 5, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ tags',
                emptyTable: 'No tags found'
            }
        });

        function relocateSearch() {
            try {
                if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
                    return;
                }

                window.PortalDataTableUi.relocateSearch({
                    filtersContainerId: 'tagsFilters',
                    dataTableFilterId: 'tagsTable_filter',
                    placeholder: 'Search tags...',
                    inputId: 'globalTagsSearch'
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

        const iboxContent = tagsTable.closest('.ibox-content');
        if (iboxContent.length && iboxContent[0] && iboxContent[0].classList) {
            iboxContent[0].classList.add('datatable-tight');
        }
    }
});
