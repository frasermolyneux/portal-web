// Game Servers Index - Initialize DataTables with responsive
$(document).ready(function () {
    const gameServersTable = $('#gameServersTable');
    
    if (gameServersTable.length && gameServersTable.find('tbody tr').length > 0) {
        const table = gameServersTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[0, 'asc']], // Position ascending
            columnDefs: [
                { targets: 0, responsivePriority: 3 }, // Position - medium priority
                { targets: 1, responsivePriority: 6 }, // Game Type - low priority
                { targets: 2, responsivePriority: 1 }, // Title - always visible
                { targets: 3, responsivePriority: 4 }, // Hostname - medium-low priority
                { targets: 4, responsivePriority: 7 }, // Live Tracking - very low
                { targets: 5, responsivePriority: 8 }, // Portal Server List - very low
                { targets: 6, responsivePriority: 9 }, // Banner Server List - very low
                { targets: 7, responsivePriority: 10 }, // Chat Log - very low
                { targets: 8, responsivePriority: 11 }, // Bot Enabled - very low
                { targets: 9, responsivePriority: 2 }  // Actions - high priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ servers',
                emptyTable: 'No game servers found'
            }
        });

        function relocateSearch() {
            try {
                const filters = document.getElementById('gameServersFilters');
                const dtFilter = document.getElementById('gameServersTable_filter');
                if (!filters || !dtFilter) return;
                if (dtFilter.classList) dtFilter.classList.add('filter-group');
                const label = dtFilter.querySelector('label');
                if (label) {
                    const input = label.querySelector('input');
                    if (input) {
                        if (input.classList) input.classList.add('form-control');
                        input.placeholder = 'Search servers...';
                        label.textContent = '';
                        const newLabel = document.createElement('label');
                        newLabel.className = 'form-label';
                        newLabel.setAttribute('for', input.id || 'globalGameServersSearch');
                        if (!input.id) input.id = 'globalGameServersSearch';
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

        const iboxContent = gameServersTable.closest('.ibox-content')[0];
        if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
    }
});
