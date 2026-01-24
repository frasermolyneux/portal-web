// IP Address Details - Players using this IP
$(document).ready(function () {
    const playersTable = $('#ipPlayersTable');
    
    if (playersTable.length && playersTable.find('tbody tr').length > 0) {
        playersTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[2, 'desc']], // Last Used desc
            columnDefs: [
                { targets: 0, responsivePriority: 1, orderable: true }, // Username - always visible
                { targets: 1, responsivePriority: 4, orderable: true }, // Added
                { targets: 2, responsivePriority: 2, orderable: true }, // Last Used
                { targets: 3, responsivePriority: 3, orderable: false }  // Confidence - not sortable
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ players',
                emptyTable: 'No players found for this IP address'
            }
        });
    }
});
