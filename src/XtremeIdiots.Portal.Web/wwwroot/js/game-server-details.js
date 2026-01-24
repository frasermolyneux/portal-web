// Game Server Details - Ban File Monitors table
$(document).ready(function () {
    const monitorsTable = $('#gameServerMonitorsTable');
    
    if (monitorsTable.length && monitorsTable.find('tbody tr').length > 0) {
        monitorsTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: false, // Usually small dataset per server
            searching: false,
            order: [[1, 'desc']], // Last Sync desc
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // File Path - always visible
                { targets: 1, responsivePriority: 3 }, // Last Sync - medium priority
                { targets: 2, responsivePriority: 2 }  // Actions - high priority
            ]
        });
    }
});
