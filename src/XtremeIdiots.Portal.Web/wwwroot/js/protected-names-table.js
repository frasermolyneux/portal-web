// Protected Names tables - Initialize DataTables with responsive
$(document).ready(function () {
    const protectedNamesTable = $('#protectedNamesTable');
    
    if (protectedNamesTable.length && protectedNamesTable.find('tbody tr').length > 0) {
        const table = protectedNamesTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[2, 'desc']], // Created date desc (column index 2)
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Name - always visible
                { targets: 1, responsivePriority: 3 }, // Owner/Player - medium priority
                { targets: 2, responsivePriority: 4 }, // Created - lower priority
                { targets: 3, responsivePriority: 5 }, // Created By - lowest priority
                { targets: 4, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No protected names found'
            }
        });

        function relocateSearch() {
            try {
                const filters = document.getElementById('protectedNamesFilters');
                const dtFilter = document.getElementById('protectedNamesTable_filter');
                if (!filters || !dtFilter) return;
                if (dtFilter.classList) dtFilter.classList.add('filter-group');
                const label = dtFilter.querySelector('label');
                if (label) {
                    const input = label.querySelector('input');
                    if (input) {
                        if (input.classList) input.classList.add('form-control');
                        input.placeholder = 'Search protected names...';
                        label.textContent = '';
                        const newLabel = document.createElement('label');
                        newLabel.className = 'form-label';
                        newLabel.setAttribute('for', input.id || 'globalProtectedNamesSearch');
                        if (!input.id) input.id = 'globalProtectedNamesSearch';
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

        const iboxContent = protectedNamesTable.closest('.ibox-content')[0];
        if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
    }
});
