// Server Admin Index - Initialize DataTables with responsive behavior
$(document).ready(function () {
    const serverTable = $('#serverAdminTable');
    
    if (serverTable.length && serverTable.find('tbody tr').length > 0) {
        serverTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: false,
            searching: false,
            order: [[0, 'asc']], // Title ascending
            columnDefs: [
                { targets: 0, responsivePriority: 1, orderable: true }, // Title - always visible
                { targets: 1, responsivePriority: 4, orderable: true }, // Hostname - collapsible
                { targets: 2, responsivePriority: 3, orderable: false }, // Players - medium priority
                { targets: 3, responsivePriority: 5, orderable: true }, // Map - collapsible first
                { targets: 4, responsivePriority: 6, orderable: true }, // Mod - collapsible first
                { targets: 5, responsivePriority: 2, orderable: false }  // Actions - always visible
            ],
            language: {
                emptyTable: 'No servers available for administration'
            },
            dom: 'rt' // Only show table (no search, length, info, or pagination)
        });
    }
});
