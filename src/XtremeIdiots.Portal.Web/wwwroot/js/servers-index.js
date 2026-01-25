// Servers Index - Initialize DataTables with responsive
$(document).ready(function () {
    const serversTable = $('#serversTable');
    
    if (serversTable.length && serversTable.find('tbody tr').length > 0) {
        const dataTable = serversTable.DataTable({
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
                { targets: 0, responsivePriority: 6, orderable: false }, // Game icon - not sortable
                { targets: 1, responsivePriority: 1 }, // Title - always visible, sortable
                { targets: 2, responsivePriority: 3 }, // Hostname - medium priority, sortable
                { targets: 3, responsivePriority: 5, orderable: false }, // Links - not sortable
                { targets: 4, responsivePriority: 4, orderable: false }, // Players - not sortable
                { targets: 5, responsivePriority: 7 }, // Map - sortable
                { targets: 6, responsivePriority: 8 }, // Mod - sortable
                { targets: 7, responsivePriority: 2, orderable: false }  // Actions - not sortable
            ],
            language: {
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No servers found'
            },
            dom: 'lfrtip' // Default layout
        });

        // Connect custom search input to DataTable
        const searchInput = $('#serversSearchInput');
        if (searchInput.length) {
            // Bind the custom search input to DataTable search
            searchInput.on('keyup search input', function () {
                dataTable.search(this.value).draw();
            });
        }

        // Hide the default DataTables search box
        $('.dataTables_filter').hide();
    }
});
