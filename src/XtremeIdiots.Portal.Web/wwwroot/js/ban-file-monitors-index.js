// Ban File Monitors Index - Initialize DataTables with responsive
$(document).ready(function () {
    const monitorsTable = $('#banFileMonitorsTable');
    
    if (monitorsTable.length && monitorsTable.find('tbody tr').length > 0) {
        monitorsTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[3, 'desc']], // Last Sync descending
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Game Server - always visible
                { targets: 1, responsivePriority: 3 }, // File Path - medium priority
                { targets: 2, responsivePriority: 5, orderable: false }, // Mod Check - not sortable
                { targets: 3, responsivePriority: 4 }, // Last Sync
                { targets: 4, responsivePriority: 2, orderable: false }  // Actions - not sortable
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ monitors',
                emptyTable: 'No ban file monitors found'
            },
            dom: 'lfrtip' // Default layout
        });

        // Move search box outside and make it full width
        setTimeout(function() {
            const searchWrapper = $('#banFileMonitorsTable_filter');
            const iboxContent = monitorsTable.closest('.ibox-content');
            
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
