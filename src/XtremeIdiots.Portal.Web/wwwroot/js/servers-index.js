// Servers Index - Initialize DataTables with responsive
$(document).ready(function () {
    const serversTable = $('#serversTable');
    
    if (serversTable.length && serversTable.find('tbody tr').length > 0) {
        serversTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[1, 'asc']], // Title ascending
            columnDefs: [
                { targets: 0, responsivePriority: 6 }, // Game icon - very low priority
                { targets: 1, responsivePriority: 1 }, // Title - always visible
                { targets: 2, responsivePriority: 3 }, // Hostname - medium priority
                { targets: 3, responsivePriority: 5 }, // Links - lower priority
                { targets: 4, responsivePriority: 4 }, // Players - medium-low priority
                { targets: 5, responsivePriority: 7 }, // Map - very low priority
                { targets: 6, responsivePriority: 8 }, // Mod - very low priority
                { targets: 7, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No servers found'
            }
        });
    }
});
