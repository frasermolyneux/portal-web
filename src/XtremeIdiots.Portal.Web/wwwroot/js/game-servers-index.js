// Game Servers Index - Initialize DataTables with responsive
$(document).ready(function () {
    const gameServersTable = $('#gameServersTable');
    
    if (gameServersTable.length && gameServersTable.find('tbody tr').length > 0) {
        gameServersTable.DataTable({
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
                { targets: 0, responsivePriority: 3 }, // Position - medium priority
                { targets: 1, responsivePriority: 6 }, // Game Type - low priority
                { targets: 2, responsivePriority: 1 }, // Title - always visible
                { targets: 3, responsivePriority: 4 }, // Hostname - medium-low priority
                { targets: 4, responsivePriority: 7 }, // Live Tracking - very low
                { targets: 5, responsivePriority: 8 }, // Portal Server List - very low
                { targets: 6, responsivePriority: 9 }, // Banner Server List - very low
                { targets: 7, responsivePriority: 10 }, // Chat Log - very low
                { targets: 8, responsivePriority: 11 }, // Bot Enabled - very low
                { targets: 9, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No game servers found'
            }
        });
    }
});
