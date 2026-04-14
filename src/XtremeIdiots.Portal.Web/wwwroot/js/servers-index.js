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
            paging: false,
            searching: false,
            info: false,
            order: [], // Preserve server-side ordering (ServerListPosition)
            columnDefs: [
                { targets: 0, responsivePriority: 6, orderable: false, width: '30px' }, // Game icon
                { targets: 1, responsivePriority: 1 }, // Title
                { targets: 2, responsivePriority: 3 }, // Hostname
                { targets: 3, responsivePriority: 5, orderable: false }, // Links
                { targets: 4, responsivePriority: 4, orderable: false }, // Players
                { targets: 5, responsivePriority: 7 }, // Map
                { targets: 6, responsivePriority: 8 }  // Mod
            ],
            language: {
                emptyTable: 'No servers found'
            },
            dom: 'rt' // Table only, no search/paging/info controls
        });
    }
});
