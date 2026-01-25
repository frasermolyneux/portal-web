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
            paging: true,
            pageLength: 25,
            searching: true,
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
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No servers available for administration'
            },
            dom: 'lfrtip' // Default layout
        });

        // Move search box and make it full width
        setTimeout(function() {
            const searchWrapper = $('#serverAdminTable_filter');
            const iboxContent = serverTable.closest('.ibox-content');
            
            if (searchWrapper.length && iboxContent.length) {
                // Create a full-width search container
                const searchContainer = $('<div class="datatable-search-wrapper mb-3"></div>');
                searchContainer.append(searchWrapper);
                iboxContent.before(searchContainer);
                
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
