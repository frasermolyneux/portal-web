// Ban File Monitors Index - Initialize DataTables with responsive
$(document).ready(function () {
    const monitorsTable = $('#banFileMonitorsTable');
    
    if (monitorsTable.length && monitorsTable.find('tbody tr').length > 0) {
        monitorsTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[3, 'desc']], // Last Sync descending
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Game Server - always visible
                { targets: 1, responsivePriority: 3 }, // File Path - medium priority
                { targets: 2, responsivePriority: 5 }, // Mod Check - lower priority
                { targets: 3, responsivePriority: 4 }, // Last Sync - lower priority
                { targets: 4, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ monitors',
                emptyTable: 'No ban file monitors found'
            }
        });
    }
});
