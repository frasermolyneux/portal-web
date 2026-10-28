// Maps Index page script extracted from inline Razor view
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[1, 'asc']],
        columnDefs: [
            { targets: 0, responsivePriority: 2, width: '30px' },  // Game (icon only)
            { targets: 1, responsivePriority: 1, width: '20%' },  // Name
            { targets: 2, responsivePriority: 5, width: '25%' },  // Map Files
            { targets: 3, responsivePriority: 3, width: '30%' },  // Popularity
            { targets: 4, responsivePriority: 4, width: '20%' }   // Image
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) {
                    xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                }
                // mutate url based on filters
                const gt = document.getElementById('filterGameType')?.value;
                let base = dataUrl.replace(/\/Maps\/GetMapListAjax.*/, '/Maps/GetMapListAjax');
                if (gt) {
                    base = dataUrl.replace(/\/Maps\/GetMapListAjax.*/, '/Maps/GetMapListAjax/' + encodeURIComponent(gt));
                }
                this.url = base;
            }
        },
        columns: [
            { data: 'gameType', name: 'gameType', orderable: true, render: function (data) { return gameTypeIcon(data); } },
            { data: 'mapName', name: 'mapName', orderable: true, render: $.fn.dataTable.render.text() },
            { data: 'mapFiles', name: 'mapFiles', orderable: false, render: function (data, type, row) { return renderMapFiles(data, row); } },
            { data: null, name: 'popularity', orderable: true, render: renderPopularity, className: 'popularity-column' },
            { data: null, orderable: false, render: renderImage }
        ]
    });

    // Move the built-in DataTables search box into our custom filters bar for unified UX
    (function relocateSearch() {
        if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
            return;
        }

        window.PortalDataTableUi.relocateSearch({
            filtersContainerId: 'mapsFilters',
            placeholder: 'Search maps...',
            inputId: 'globalMapsSearch'
        });
    })();

    function renderMapFiles(data, row) {
        const container = document.createElement('div');
        const games = { CallOfDuty2: 'cod2', CallOfDuty4: 'cod4', CallOfDuty4x: 'cod4', CallOfDuty5: 'cod5' };
        const files = Array.isArray(data) ? data.filter(file => file?.fileName && safeUrl(file.url)) : [];
        if (files.length && games[row?.gameType] && !row?.builtIn) {
            const native = document.createElement('a');
            native.href = '/MapDownloads?' + new URLSearchParams({ game: games[row.gameType], map: row.mapName });
            native.textContent = 'Download';
            native.className = 'btn btn-outline-secondary btn-sm';
            container.append(native);
            const count = document.createElement('span');
            count.className = 'small text-muted ms-2';
            count.textContent = `${files.length} ${files.length === 1 ? 'file' : 'files'}`;
            container.append(count);
            return container.innerHTML;
        }
        if (data && data.length) {
            const list = document.createElement('ul');
            list.className = 'list-unstyled mb-0 map-files';
            for (const f of data) {
                if (f && f.url && f.fileName) {
                    const item = document.createElement('li');
                    const url = safeUrl(f.url);
                    const label = document.createElement(url ? 'a' : 'span');
                    if (url) label.href = url;
                    label.textContent = f.fileName + (url ? '' : ' (download unavailable)');
                    item.append(label);
                    list.append(item);
                }
            }
            container.append(list);
        } else {
            const label = document.createElement('span');
            label.className = row?.builtIn ? 'badge bg-info' : 'text-muted';
            label.textContent = row?.builtIn ? 'Built-in' : 'No Map Files';
            container.append(label);
        }
        return container.innerHTML;
    }

    function safeUrl(value) {
        if (typeof value !== 'string' || !value.trim()) return null;
        try {
            const url = new URL(value, window.location.origin);
            return ['https:', 'http:'].includes(url.protocol) ? url.href : null;
        } catch {
            return null;
        }
    }

    function renderPopularity(data, type, row) {
        let likePct = row.likePercentage || 0;
        let dislikePct = row.dislikePercentage || 0;
        const totalLikes = row.totalLikes || 0;
        const totalDislikes = row.totalDislikes || 0;
        const totalVotes = row.totalVotes || 0;

        // If API returns values 0-1 treat as proportions
        if (likePct <= 1 && dislikePct <= 1 && (likePct + dislikePct) <= 2) {
            likePct *= 100;
            dislikePct *= 100;
        }
        // Derive if counts exist but percentages zero
        if (totalVotes > 0 && likePct === 0 && dislikePct === 0 && (totalLikes > 0 || totalDislikes > 0)) {
            likePct = totalLikes / totalVotes * 100;
            dislikePct = totalDislikes / totalVotes * 100;
        }
        // Ensure tiny bars show
        if (likePct > 0 && likePct < 1) likePct = 1;
        if (dislikePct > 0 && dislikePct < 1) dislikePct = 1;

        const likePctStr = likePct.toFixed(2);
        const dislikePctStr = dislikePct.toFixed(2);
        return '<div class="progress" id="progress-' + encodeURIComponent(row.mapName ?? '') + '" style="width:100%;min-width:140px;max-width:260px;">' +
            '<div class="progress-bar bg-info" role="progressbar" style="width: ' + likePctStr + '%" aria-valuenow="' + totalLikes + '" aria-valuemin="0" aria-valuemax="' + totalVotes + '"></div>' +
            '<div class="progress-bar bg-danger" role="progressbar" style="width: ' + dislikePctStr + '%" aria-valuenow="' + totalDislikes + '" aria-valuemin="0" aria-valuemax="' + totalVotes + '"></div>' +
            '</div>' +
            '<div class="m-t-sm">' + totalLikes + ' likes and ' + totalDislikes + ' dislikes out of ' + totalVotes + '</div>';
    }

    function renderImage(data, type, row) {
        const image = document.createElement('img');
        const src = safeUrl(row.mapImageUri);
        image.src = src || '/images/noimage.jpg';
        image.alt = src ? ('Map image for ' + row.mapName) : 'No map image available';
        image.className = 'map-image img-fluid';
        image.loading = 'lazy';
        return image.outerHTML;
    }

    function applyGameColumnVisibility() {
        const hasSpecificGame = document.getElementById('filterGameType')?.value !== '';
        table.column(0).visible(!hasSpecificGame);
        table.columns.adjust();
    }

    document.getElementById('filterGameType')?.addEventListener('change', function () {
        applyGameColumnVisibility();
        table.ajax.reload(null, false);
    });

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        const sel = document.getElementById('filterGameType');
        let changed = false;
        if (sel && sel.value !== '') {
            sel.value = '';
            applyGameColumnVisibility();
            changed = true;
        }
        // Clear global search box
        if (table.search()) {
            table.search('');
            changed = true;
        }
        // Always reset to first page when clearing filters/search
        if (changed) {
            table.page('first');
        }
        table.draw(false);
    });

    applyGameColumnVisibility();

    // Reduce internal padding under the table region
    tableEl.closest('.ibox-content').addClass('datatable-tight');
});
