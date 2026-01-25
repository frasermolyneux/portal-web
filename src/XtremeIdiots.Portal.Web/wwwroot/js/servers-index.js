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
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No servers found'
            },
            dom: 'lfrtip' // Default layout
        });

        // Move search box outside and make it full width
        setTimeout(function() {
            const searchWrapper = $('#serversTable_filter');
            const tableWrapper = $('.ibox-content');
            
            if (searchWrapper.length && tableWrapper.length) {
                // Create a full-width search container
                const searchContainer = $('<div class="datatable-search-wrapper mb-3"></div>');
                searchContainer.append(searchWrapper);
                tableWrapper.before(searchContainer);
                
                // Style the search input to be full width
                const searchInput = searchWrapper.find('input');
                if (searchInput.length) {
                    searchInput.addClass('form-control').css('width', '100%');
                    searchWrapper.css('width', '100%');
                    
                    // Update label
                    const label = searchWrapper.find('label');
                    if (label.length) {
                        label.contents().filter(function() {
                            return this.nodeType === 3;
                        }).remove();
                        label.prepend('Search: ');
                    }
                }
            }
        }, 100);
    }
});
