// Server Info - Initialize DataTables for player list and map rotation
$(document).ready(function () {
    // Connected Players Table  
    const playersTable = $('#connectedPlayersTable');
    if (playersTable.length && playersTable.find('tbody tr').length > 0) {
        playersTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: false, // Live data, usually not many players
            searching: false,
            order: [[0, 'asc']], // Num ascending
            columnDefs: [
                { targets: 0, responsivePriority: 3, orderable: true }, // Num
                { targets: 1, responsivePriority: 1, orderable: true }, // Name - always visible
                { targets: 2, responsivePriority: 2, orderable: true }  // Score
            ]
        });
    }

    // Map Rotation Table
    const mapRotationTable = $('#serverMapRotationTable');
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
                { targets: 0, responsivePriority: 1, orderable: true }, // Name - always visible
                { targets: 1, responsivePriority: 4, orderable: false }, // Files - not sortable
                { targets: 2, responsivePriority: 3, orderable: false }, // Popularity - not sortable
                { targets: 3, responsivePriority: 5, orderable: false }  // Image - not sortable
            ]
        });
    }
});
