// Protected Names tables - Initialize DataTables with responsive
$(document).ready(function () {
    const protectedNamesTable = $('#protectedNamesTable');
    
    if (protectedNamesTable.length && protectedNamesTable.find('tbody tr').length > 0) {
        protectedNamesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[2, 'desc']], // Created date desc (column index 2)
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 3 }, // Owner/Player - medium priority
                { targets: 2, responsivePriority: 4 }, // Created - lower priority
                { targets: 3, responsivePriority: 5 }, // Created By - lowest priority
                { targets: 4, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No protected names found'
            }
        });
    }
});
