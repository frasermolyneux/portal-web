// Tags Index - Initialize DataTables with responsive
$(document).ready(function () {
    const tagsTable = $('#tagsTable');
    
    if (tagsTable.length && tagsTable.find('tbody tr').length > 0) {
        const table = tagsTable.DataTable({
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

        function relocateSearch() {
            try {
                const filters = document.getElementById('tagsFilters');
                const dtFilter = document.getElementById('tagsTable_filter');
                if (!filters || !dtFilter) return;
                if (dtFilter.classList) dtFilter.classList.add('filter-group');
                const label = dtFilter.querySelector('label');
                if (label) {
                    const input = label.querySelector('input');
                    if (input) {
                        if (input.classList) input.classList.add('form-control');
                        input.placeholder = 'Search tags...';
                        label.textContent = '';
                        const newLabel = document.createElement('label');
                        newLabel.className = 'form-label';
                        newLabel.setAttribute('for', input.id || 'globalTagsSearch');
                        if (!input.id) input.id = 'globalTagsSearch';
                        newLabel.textContent = 'Search';
                        dtFilter.appendChild(newLabel);
                        dtFilter.appendChild(input);
                    }
                }
                const resetBtn = document.getElementById('resetFilters');
                const resetGroup = resetBtn ? resetBtn.closest('.filter-group') : null;
                if (resetGroup && resetGroup.parentElement === filters) {
                    filters.insertBefore(dtFilter, resetGroup);
                } else {
                    filters.appendChild(dtFilter);
                }
            } catch { /* swallow */ }
        }

        table.on('init.dt', function () { relocateSearch(); });
        setTimeout(relocateSearch, 1000);

        document.getElementById('resetFilters')?.addEventListener('click', function () {
            if (table.search()) {
                table.search('').draw();
            }
        });

        const iboxContent = tagsTable.closest('.ibox-content')[0];
        if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
    }
});
