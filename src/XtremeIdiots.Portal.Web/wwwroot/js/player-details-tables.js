// Players Details page - Initialize DataTables with responsive on static tables
$(document).ready(function () {
    // Aliases Table
    const aliasesTable = $('#aliasesTable');
    if (aliasesTable.length && aliasesTable.find('tbody tr').length > 0) {
        aliasesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 10,
            order: [[2, 'desc']], // Last Used desc
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 4 }, // Added - lower priority
                { targets: 2, responsivePriority: 2 }, // Last Used - high priority
                { targets: 3, responsivePriority: 3 }  // Confidence - medium priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No aliases found'
            }
        });
    }

    // IP Addresses Table
    const ipAddressesTable = $('#ipAddressesTable');
    if (ipAddressesTable.length && ipAddressesTable.find('tbody tr').length > 0) {
        ipAddressesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 10,
            order: [[2, 'desc']], // Last Used desc
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Address - always visible
                { targets: 1, responsivePriority: 4 }, // Added - lower priority
                { targets: 2, responsivePriority: 2 }, // Last Used - high priority
                { targets: 3, responsivePriority: 3 }  // Confidence - medium priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No IP addresses found'
            }
        });
    }

    // Protected Names Table
    const protectedNamesTable = $('#protectedNamesTable');
    if (protectedNamesTable.length && protectedNamesTable.find('tbody tr').length > 0) {
        protectedNamesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 10,
            order: [[1, 'desc']], // Created desc
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 3 }, // Created - medium priority
                { targets: 2, responsivePriority: 4 }, // Created By - lower priority
                { targets: 3, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No protected names found'
            }
        });
    }
});
