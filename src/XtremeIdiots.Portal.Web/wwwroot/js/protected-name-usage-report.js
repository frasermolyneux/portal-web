// Protected Name Usage Report table - Initialize DataTables with responsive
$(document).ready(function () {
    const usageReportTable = $('#usageReportTable');
    
    if (usageReportTable.length && usageReportTable.find('tbody tr').length > 0) {
        usageReportTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[1, 'desc']], // Last Used desc (column index 1)
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Player - always visible
                { targets: 1, responsivePriority: 4 }, // Last Used - lower priority  
                { targets: 2, responsivePriority: 2 }, // Times Used - high priority
                { targets: 3, responsivePriority: 3 }  // Status - medium priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No usage instances found'
            }
        });
    }
});
