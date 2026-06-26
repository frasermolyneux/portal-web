// Players Index page script (refactored: removed separate Game column, added IP column)
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');
    const playersFilterSel = document.getElementById('filterPlayersFilter');
    const playerTagSel = document.getElementById('filterPlayerTag');

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[5, 'desc']], // Last Seen desc (index 5 after Name, Tags, IP, Guid, FirstSeen, LastSeen)
        stateSaveParams: function (settings, data) {
            data._playersStructureVersion = 5; // bump when changing column/filter persistence structure
            if (data.columns) data.columns.forEach(function (c) { delete c.visible; });
            if (playersFilterSel) data.playersFilter = playersFilterSel.value || 'UsernameAndGuid';
            const gtSel = document.getElementById('filterGameType');
            if (gtSel) data.gameType = gtSel.value || '';
            if (playerTagSel) data.playerTagId = playerTagSel.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._playersStructureVersion !== 5) {
                var key = 'DataTables_dataTable_' + window.location.pathname;
                try { localStorage.removeItem(key); } catch (e) { /* ignore */ }
                return false;
            }
            if (data.columns) data.columns.forEach(function (c) { delete c.visible; });
            if (playersFilterSel && data.playersFilter) playersFilterSel.value = data.playersFilter;
            const gtSel = document.getElementById('filterGameType');
            if (gtSel && typeof data.gameType !== 'undefined') gtSel.value = data.gameType;
            if (playerTagSel && typeof data.playerTagId !== 'undefined') playerTagSel.value = data.playerTagId;
        },
        columnDefs: [
            { targets: 0, responsivePriority: 1, visible: true }, // Name (force visible)
            { targets: 1, responsivePriority: 4, visible: true }, // Tags
            { targets: 2, responsivePriority: 2, visible: true }, // Player IP (force visible)
            { targets: 3, responsivePriority: 5 }, // Guid
            { targets: 4, responsivePriority: 6 }, // First Seen
            { targets: 5, responsivePriority: 3 }  // Last Seen
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                const gt = document.getElementById('filterGameType')?.value;
                const pf = playersFilterSel?.value || 'UsernameAndGuid';
                const tagId = playerTagSel?.value;
                let base = dataUrl;
                // Build up URL: /Players/GetPlayersAjax[/GameType]?filter=PlayersFilterValue
                if (gt) base = dataUrl.replace(/\/Players\/GetPlayersAjax.*/, '/Players/GetPlayersAjax/' + encodeURIComponent(gt));
                const urlObj = new URL(base, window.location.origin);
                urlObj.searchParams.set('playersFilter', pf);
                if (tagId) {
                    urlObj.searchParams.set('selectedTagId', tagId);
                } else {
                    urlObj.searchParams.delete('selectedTagId');
                }
                this.url = urlObj.pathname + urlObj.search;
            }
        },
        columns: [
            { data: 'username', name: 'username', orderable: true, render: function (data, type, row) { return renderPlayerName(row['gameType'], row['username'], row['playerId']); } },
            { data: 'tags', name: 'tags', orderable: false, render: function (data, type) { return renderPlayerTags(data, type); } },
            {
                data: 'ipAddress', name: 'ipAddress', orderable: false, render: function (data, type, row) {
                    return data ? formatIPAddress(data, row['proxyCheckRiskScore'], row['isProxy'], row['isVpn'], row['proxyType'], row['countryCode'], true) : '';
                }
            },
            { data: 'guid', name: 'guid', orderable: false, defaultContent: '' },
            { data: 'firstSeen', name: 'firstSeen', orderable: true, render: function (data) { return data ? ('<span title="' + portalDate.formatDateTime(data) + '">' + portalDate.formatDateTime(data, { showRelative: true }) + '</span>') : ''; } },
            { data: 'lastSeen', name: 'lastSeen', orderable: true, render: function (data) { return data ? ('<span title="' + portalDate.formatDateTime(data) + '">' + portalDate.formatRelativeTime(data) + '</span>') : ''; } }
        ]
    });

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/\"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function renderPlayerTags(tags, type) {
        if (!Array.isArray(tags) || tags.length === 0) {
            return type === 'display' ? '<span class="text-muted">-</span>' : '';
        }

        if (type !== 'display') {
            return tags
                .map(function (t) { return t?.name || ''; })
                .filter(function (n) { return n.length > 0; })
                .join(', ');
        }

        const maxVisibleTags = 3;
        const visibleTagHtml = tags
            .slice(0, maxVisibleTags)
            .map(function (tag) {
                if (tag?.name) {
                    return '<span class="badge bg-secondary me-1">' + escapeHtml(tag.name) + '</span>';
                }

                return '';
            })
            .join('');

        const remainingTagCount = tags.length - maxVisibleTags;
        const overflow = remainingTagCount > 0
            ? '<span class="badge bg-light text-dark border">+' + remainingTagCount + '</span>'
            : '';

        return '<div class="d-flex flex-wrap align-items-center">' + visibleTagHtml + overflow + '</div>';
    }

    function relocateSearch() {
        try {
            if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
                return;
            }

            window.PortalDataTableUi.relocateSearch({
                filtersContainerId: 'playersFilters',
                placeholder: 'Search players...',
                inputId: 'globalPlayersSearch'
            });
        } catch { /* swallow */ }
    }

    table.on('init.dt', function () {
        relocateSearch();
        if (window.PortalDataTableUi && typeof window.PortalDataTableUi.attachPageJump === 'function') {
            window.PortalDataTableUi.attachPageJump(table, { label: 'Go to page' });
        }
        table.columns.adjust().responsive.recalc();
    });
    setTimeout(function () {
        relocateSearch();
        if (window.PortalDataTableUi && typeof window.PortalDataTableUi.attachPageJump === 'function') {
            window.PortalDataTableUi.attachPageJump(table, { label: 'Go to page' });
        }
        if (table.responsive) table.columns.adjust().responsive.recalc();
    }, 1000);

    function reloadTable() { table.ajax.reload(null, false); }

    document.getElementById('filterGameType')?.addEventListener('change', reloadTable);
    playersFilterSel?.addEventListener('change', reloadTable);
    playerTagSel?.addEventListener('change', reloadTable);

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        const sel = document.getElementById('filterGameType');
        let changed = false;
        if (sel && sel.value !== '') { sel.value = ''; changed = true; }
        if (playersFilterSel && playersFilterSel.value !== 'UsernameAndGuid') { playersFilterSel.value = 'UsernameAndGuid'; changed = true; }
        if (playerTagSel && playerTagSel.value !== '') { playerTagSel.value = ''; changed = true; }
        if (table.search()) { table.search(''); changed = true; }
        if (changed) table.page('first');
        table.draw(false);
    });

    const iboxContent = tableEl.closest('.ibox-content')[0];
    if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
});
