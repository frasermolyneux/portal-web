// Tags Index - Initialize DataTables with responsive
$(document).ready(function () {
    const tagsTable = $('#tagsTable');
    
    if (tagsTable.length && tagsTable.find('tbody tr').length > 0) {
        tagsTable.DataTable({
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
                { targets: 1, responsivePriority: 4 }, // Description - lower priority
                { targets: 2, responsivePriority: 3 }, // Tag Preview - medium priority
                { targets: 3, responsivePriority: 5 }, // Players count - lowest priority
                { targets: 4, responsivePriority: 6 }, // User Defined - very low priority
                { targets: 5, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ tags',
                emptyTable: 'No tags found'
            }
        });
    }
});
