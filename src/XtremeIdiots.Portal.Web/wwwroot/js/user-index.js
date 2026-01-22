// Users index page script (aligned with players-index.js pattern)
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const userFlagSel = document.getElementById('filterUserFlag');

    // Whitelisted claim types to show as roles
    const ROLE_CLAIM_TYPES = new Set([
        'SeniorAdmin', 'HeadAdmin', 'GameAdmin', 'Moderator', 'ServerAdmin', 'BanFileMonitor', 'RconCredentials', 'FtpCredentials'
    ]);

    function renderRoles(row) {
        const claims = row.userProfileClaims || [];
        if (!Array.isArray(claims) || claims.length === 0) return '';
        const types = [...new Set(claims
            .map(c => c.claimType)
            .filter(t => ROLE_CLAIM_TYPES.has(t)))];
        if (types.length === 0) return '';
        return types.map(t => '<span class="badge text-bg-secondary me-1 mb-1">' + t + '</span>').join('');
    }

    function forumProfileLink(id, displayName) {
        if (!id) return '';
        const name = (displayName || '').toString().trim().toLowerCase();
        if (!name) return id; // fallback no link if no name
        const slug = name
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')
            .substring(0, 60); // trim excessively long
        const url = 'https://www.xtremeidiots.com/profile/' + id + '-' + slug + '/';
        return '<a href="' + url + '" target="_blank" rel="noopener noreferrer" title="Open forum profile in new tab">' + id + '</a>';
    }

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        order: [[2, 'asc']], // displayName column
        stateSaveParams: function (settings, data) {
            data._usersStructureVersion = 1;
            if (userFlagSel) data.userFlag = userFlagSel.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._usersStructureVersion !== 1) return false;
            if (userFlagSel && typeof data.userFlag !== 'undefined') userFlagSel.value = data.userFlag;
        },
        columnDefs: [
            { targets: 2, responsivePriority: 1 }, // Username
            { targets: 3, responsivePriority: 2 }, // Email
            { targets: 4, responsivePriority: 5 }, // Roles
            { targets: 5, responsivePriority: 3 }  // Logout
        ],
        ajax: {
            url: '/User/GetUsersAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                if (tokenInput) xhr.setRequestHeader('RequestVerificationToken', tokenInput.value);
                const baseUrl = '/User/GetUsersAjax';
                const flagVal = userFlagSel?.value;
                this.url = flagVal ? (baseUrl + '?userFlag=' + encodeURIComponent(flagVal)) : baseUrl;
            }
        },
        columns: [
            { data: 'xtremeIdiotsForumId', name: 'xtremeIdiotsForumId', sortable: false, render: function (data, type, row) { return forumProfileLink(data, row.displayName); } },
            { data: 'userProfileId', name: 'userProfileId', sortable: false, render: function (data) { return '<a href="/User/ManageProfile/' + data + '">' + data + '</a>'; } },
            { data: 'displayName', name: 'displayName', sortable: true },
            { data: 'email', name: 'email', sortable: false },
            { data: null, name: 'roles', sortable: false, defaultContent: '', render: function (data, type, row) { return renderRoles(row); } },
            { data: null, defaultContent: '', sortable: false, render: function (data, type, row) {
                const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
                const token = tokenInput ? tokenInput.value : '';
                return logOutUserLink(row['xtremeIdiotsForumId'], '<input name="__RequestVerificationToken" type="hidden" value="' + token + '" />');
            } }
        ]
    });

    function relocateSearch() {
        try {
            const filters = document.getElementById('usersFilters');
            const dtFilter = document.getElementById('dataTable_filter');
            if (!filters || !dtFilter) return;
            if (dtFilter.classList) dtFilter.classList.add('filter-group');
            const label = dtFilter.querySelector('label');
            if (label) {
                const input = label.querySelector('input');
                if (input) {
                    if (input.classList) input.classList.add('form-control');
                    input.placeholder = 'Search users...';
                    label.textContent = '';
                    const newLabel = document.createElement('label');
                    newLabel.className = 'form-label';
                    newLabel.setAttribute('for', input.id || 'globalUsersSearch');
                    if (!input.id) input.id = 'globalUsersSearch';
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

    table.on('init.dt', function(){ relocateSearch(); });
    setTimeout(relocateSearch, 1000);

    function reloadTable() { table.ajax.reload(null, false); }

    userFlagSel?.addEventListener('change', reloadTable);

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        let changed = false;
        if (userFlagSel && userFlagSel.value !== '') { userFlagSel.value = ''; changed = true; }
        if (table.search()) { table.search(''); changed = true; }
        if (changed) table.page('first');
        table.draw(false);
    });

    const iboxContent = tableEl.closest('.ibox-content')[0];
    if (iboxContent && iboxContent.classList) iboxContent.classList.add('datatable-tight');
});
