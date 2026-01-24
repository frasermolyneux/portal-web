// Map Manager - Initialize DataTables with responsive for all tables
$(document).ready(function () {
    // Map Packs Table
    const mapPacksTable = $('#mapPacksTable');
    if (mapPacksTable.length && mapPacksTable.find('tbody tr').length > 0) {
        mapPacksTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: false, // Small dataset
            searching: false,
            order: [[0, 'asc']], // Title ascending
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Title - always visible
                { targets: 1, responsivePriority: 3 }, // Game Mode - medium priority
                { targets: 2, responsivePriority: 4 }, // Count - lower priority
                { targets: 3, responsivePriority: 2 }  // Actions - high priority
            ]
        });
    }

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
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 4 }, // Files - lower priority
                { targets: 2, responsivePriority: 3 }, // Remote Status - medium priority
                { targets: 3, responsivePriority: 5 }, // Popularity - low priority
                { targets: 4, responsivePriority: 6 }  // Image - very low priority
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
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 4 }, // Path - lower priority
                { targets: 2, responsivePriority: 3 }, // Rotation Status - medium priority
                { targets: 3, responsivePriority: 5 }, // Modified - low priority
                { targets: 4, responsivePriority: 2 }  // Actions - high priority
            ]
        });
    }
});
