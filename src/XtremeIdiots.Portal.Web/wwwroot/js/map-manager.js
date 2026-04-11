// Map Manager - Initialize DataTables with responsive for all tables
$(document).ready(function () {
    // Current Map Rotation Table
    const mapRotationTable = $('#mapRotationTable');
    if (mapRotationTable.length && mapRotationTable.find('tbody tr').length > 0) {
        mapRotationTable.DataTable({
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
                { targets: 0, responsivePriority: 1, orderable: true },  // Name - always visible
                { targets: 1, responsivePriority: 4, orderable: false }, // Game Type - not sortable
                { targets: 2, responsivePriority: 5, orderable: false }, // Files - not sortable
                { targets: 3, responsivePriority: 3, orderable: false }, // Remote Status - not sortable
                { targets: 4, responsivePriority: 6, orderable: false }, // Popularity - not sortable
                { targets: 5, responsivePriority: 7, orderable: false }  // Image - not sortable
            ]
        });
    }

    // Remote Server Maps Table
    const remoteMapsTable = $('#remoteMapsTable');
    if (remoteMapsTable.length && remoteMapsTable.find('tbody tr').length > 0) {
        remoteMapsTable.DataTable({
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
                { targets: 0, responsivePriority: 1, orderable: true }, // Name - always visible
                { targets: 1, responsivePriority: 4, orderable: false }, // Path - not sortable
                { targets: 2, responsivePriority: 3, orderable: false }, // Rotation Status - not sortable
                { targets: 3, responsivePriority: 5, orderable: false }, // Modified - not sortable
                { targets: 4, responsivePriority: 2, orderable: false }  // Actions - not sortable
            ]
        });
    }
});
