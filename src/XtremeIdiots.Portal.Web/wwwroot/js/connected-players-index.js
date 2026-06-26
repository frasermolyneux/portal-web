$(document).ready(function () {
    const tableEl = $('#dataTable');
    const gameSel = document.getElementById('gameType');
    const statusSel = document.getElementById('isActive');
    const canUnlink = tableEl.data('can-unlink') === true || tableEl.data('can-unlink') === 'true';
    const manualLinkForm = document.getElementById('manualLinkForm');

    if (!tableEl.length) {
        return;
    }

    function htmlEncode(value) {
        return $('<div/>').text(value ?? '').html();
    }

    function renderUserProfileLink(userProfileId) {
        const routeValue = encodeURIComponent(userProfileId ?? '');
        return '<a href="/User/ManageProfile/' + routeValue + '">Link</a>';
    }

    function renderGameIcon(gameType) {
        return gameTypeIcon(htmlEncode(gameType));
    }

    function renderUsernameLink(username, playerId) {
        const routeValue = encodeURIComponent(playerId ?? '');
        const coloredName = typeof CodColors !== 'undefined' && CodColors && typeof CodColors.renderSafe === 'function'
            ? CodColors.renderSafe(username)
            : htmlEncode(username);

        if (!playerId) {
            return coloredName;
        }

        return '<a href="/Players/Details/' + routeValue + '">' + coloredName + '</a>';
    }

    function renderStatusBadge(isActive) {
        return isActive
            ? '<span class="badge bg-success">Active</span>'
            : '<span class="badge bg-secondary">Inactive</span>';
    }

    function renderTimeCell(utcDate) {
        if (!utcDate) {
            return '';
        }

        const formatted = portalDate.formatDateTime(utcDate);
        return '<span title="' + htmlEncode(formatted) + '">' + htmlEncode(formatted) + '</span>';
    }

    function renderUnlinkAction(row) {
        if (!canUnlink || !row.isActive) {
            return '';
        }

        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';
        const id = htmlEncode(row.connectedPlayerProfileId);

        return '' +
            '<form action="/ConnectedPlayers/ForceUnlink" method="post" class="d-inline">' +
            '<input name="__RequestVerificationToken" type="hidden" value="' + htmlEncode(token) + '" />' +
            '<input type="hidden" name="connectedPlayerProfileId" value="' + id + '" />' +
            '<button type="submit" class="btn btn-outline-danger btn-sm" data-confirm="Are you sure you want to unlink this connected player?">' +
            '<i class="fa-solid fa-fw fa-link-slash" aria-hidden="true"></i> Unlink' +
            '</button>' +
            '</form>';
    }

    function getAntiForgeryToken() {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function showToastSuccess(message) {
        if (window.toastr && typeof window.toastr.success === 'function') {
            window.toastr.success(message);
            return;
        }

        window.alert(message);
    }

    function showToastError(message) {
        if (window.toastr && typeof window.toastr.error === 'function') {
            window.toastr.error(message);
            return;
        }

        window.alert(message);
    }

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        responsive: { details: { type: 'inline', target: 'tr' } },
        autoWidth: false,
        stateSave: true,
        stateSaveParams: function (settings, data) {
            data._connectedPlayersStructureVersion = 2;
            data.filterGameType = gameSel?.value || '';
            data.filterStatus = statusSel?.value || '';
        },
        stateLoadParams: function (settings, data) {
            if (data._connectedPlayersStructureVersion !== 2) {
                return false;
            }

            if (gameSel && typeof data.filterGameType !== 'undefined') {
                gameSel.value = data.filterGameType;
            }

            if (statusSel && typeof data.filterStatus !== 'undefined') {
                statusSel.value = data.filterStatus;
            }
        },
        pageLength: 25,
        order: [[5, 'desc']],
        ajax: {
            url: '/ConnectedPlayers/GetConnectedPlayersAjax',
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const token = getAntiForgeryToken();
                if (token) {
                    xhr.setRequestHeader('RequestVerificationToken', token);
                }

                const url = new URL('/ConnectedPlayers/GetConnectedPlayersAjax', window.location.origin);
                if (gameSel?.value) {
                    url.searchParams.set('gameType', gameSel.value);
                }

                if (statusSel?.value) {
                    url.searchParams.set('isActive', statusSel.value === 'Active' ? 'true' : 'false');
                }

                this.url = url.pathname + url.search;
            }
        },
        columnDefs: [
            { targets: 0, responsivePriority: 3 },
            { targets: 1, responsivePriority: 1 },
            { targets: 2, responsivePriority: 4 },
            { targets: 3, responsivePriority: 8 },
            { targets: 4, responsivePriority: 2 },
            { targets: 5, responsivePriority: 6 },
            { targets: 6, responsivePriority: 9 },
            { targets: 7, responsivePriority: 7, orderable: false, searchable: false }
        ],
        columns: [
            { data: 'gameType', name: 'gameType', orderable: true, render: function (data) { return renderGameIcon(data); } },
            { data: 'username', name: 'username', orderable: true, render: function (data, type, row) { return renderUsernameLink(data, row.playerId); } },
            { data: 'userProfileId', name: 'userProfileId', orderable: false, render: function (data) { return renderUserProfileLink(data); } },
            { data: 'linkMethod', name: 'linkMethod', orderable: true, render: function (data) { return htmlEncode(data); } },
            { data: 'isActive', name: 'isActive', orderable: true, render: function (data, type, row) { return renderStatusBadge(row.isActive); } },
            { data: 'linkedAtUtc', name: 'linkedAtUtc', orderable: true, render: function (data) { return renderTimeCell(data); } },
            { data: 'unlinkedAtUtc', name: 'unlinkedAtUtc', orderable: true, render: function (data) { return renderTimeCell(data); } },
            { data: null, defaultContent: '', orderable: false, searchable: false, render: function (data, type, row) { return renderUnlinkAction(row); } }
        ],
        language: {
            emptyTable: 'No connected player links found.'
        }
    });

    function relocateSearch() {
        try {
            if (!window.PortalDataTableUi || typeof window.PortalDataTableUi.relocateSearch !== 'function') {
                return;
            }

            window.PortalDataTableUi.relocateSearch({
                filtersContainerId: 'connectedPlayersFilters',
                placeholder: 'Search connected players...',
                inputId: 'globalConnectedPlayersSearch'
            });
        } catch {
            // Intentionally no-op; table remains functional if search relocation fails.
        }
    }

    table.on('init.dt', function () {
        relocateSearch();
        table.columns.adjust().responsive.recalc();
    });

    setTimeout(function () {
        relocateSearch();
        if (table.responsive) {
            table.columns.adjust().responsive.recalc();
        }
    }, 1000);

    function applyFilters() {
        table.ajax.reload(null, false);
    }

    gameSel?.addEventListener('change', applyFilters);
    statusSel?.addEventListener('change', applyFilters);

    document.getElementById('resetFilters')?.addEventListener('click', function () {
        let changed = false;

        if (gameSel && gameSel.value !== '') {
            gameSel.value = '';
            changed = true;
        }

        if (statusSel && statusSel.value !== '') {
            statusSel.value = '';
            changed = true;
        }

        if (table.search()) {
            table.search('');
            changed = true;
        }

        if (changed) {
            table.column(0).search('', true, false);
            table.column(4).search('', true, false);
            table.page('first');
            table.draw(false);
        }
    });

    const iboxContent = tableEl.closest('.ibox-content')[0];
    if (iboxContent && iboxContent.classList) {
        iboxContent.classList.add('datatable-tight');
    }

    function initAutocomplete(options) {
        const cfg = Object.assign({
            minLength: 2,
            delay: 250,
            mapResultText: function (item) { return item.text || ''; },
            mapResultValue: function (item) { return item.id; },
            mapEmptyMessage: function () { return 'No matches found.'; },
            queryParams: function () { return {}; },
            onSelect: function () { }
        }, options || {});

        const input = document.querySelector(cfg.inputSelector);
        const hidden = document.querySelector(cfg.hiddenSelector);
        if (!input || !hidden) {
            return null;
        }

        const box = document.createElement('div');
        box.className = 'user-suggestions list-group position-absolute bg-white shadow';
        box.setAttribute('role', 'listbox');
        box.setAttribute('aria-label', 'Suggestions');
        box.style.zIndex = '1050';
        box.style.maxHeight = '240px';
        box.style.overflowY = 'auto';
        box.style.overflowX = 'hidden';
        box.style.display = 'none';
        box.style.position = 'absolute';
        box.style.left = '0px';
        box.style.top = '0px';

        document.body.appendChild(box);

        let timer = null;
        let requestCounter = 0;

        function positionSuggestions() {
            const rect = input.getBoundingClientRect();
            box.style.width = rect.width + 'px';
            box.style.left = (window.scrollX + rect.left) + 'px';
            box.style.top = (window.scrollY + rect.bottom) + 'px';
        }

        function clearSuggestions() {
            box.innerHTML = '';
            box.style.display = 'none';
        }

        function setSuggestions(items) {
            box.innerHTML = '';

            if (!Array.isArray(items) || items.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'list-group-item text-muted py-1 px-2';
                empty.textContent = cfg.mapEmptyMessage();
                box.appendChild(empty);
                positionSuggestions();
                box.style.display = 'block';
                return;
            }

            items.forEach(function (item, index) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'list-group-item list-group-item-action py-1 px-2';
                button.setAttribute('role', 'option');
                button.id = 'connected-players-sugg-' + index;
                button.textContent = cfg.mapResultText(item);
                button.dataset.value = cfg.mapResultValue(item);
                button.addEventListener('click', function () {
                    input.value = cfg.mapResultText(item);
                    hidden.value = cfg.mapResultValue(item);
                    clearSuggestions();
                    cfg.onSelect(item);
                });
                box.appendChild(button);
            });

            positionSuggestions();
            box.style.display = 'block';
        }

        async function doSearch(term) {
            if (!term || term.length < cfg.minLength) {
                clearSuggestions();
                return;
            }

            const currentRequest = ++requestCounter;
            const params = new URLSearchParams(cfg.queryParams(term));
            params.set('term', term);

            const url = cfg.searchUrl + '?' + params.toString();
            try {
                const response = await fetch(url, {
                    method: 'GET',
                    credentials: 'same-origin'
                });

                if (!response.ok) {
                    clearSuggestions();
                    return;
                }

                const data = await response.json();
                if (currentRequest !== requestCounter) {
                    return;
                }

                setSuggestions(data);
            } catch {
                clearSuggestions();
            }
        }

        input.addEventListener('input', function () {
            hidden.value = '';
            clearTimeout(timer);
            const term = (input.value || '').trim();
            timer = setTimeout(function () { doSearch(term); }, cfg.delay);
        });

        input.addEventListener('focus', function () {
            if (box.style.display !== 'none') {
                positionSuggestions();
            }
        });

        window.addEventListener('resize', function () {
            if (box.style.display !== 'none') {
                positionSuggestions();
            }
        });

        window.addEventListener('scroll', function () {
            if (box.style.display !== 'none') {
                positionSuggestions();
            }
        }, true);

        box.addEventListener('wheel', function (event) {
            event.stopPropagation();
        }, { passive: true });

        document.addEventListener('click', function (event) {
            const withinInput = input.contains(event.target);
            const withinBox = box.contains(event.target);
            if (!withinInput && !withinBox) {
                clearSuggestions();
            }
        });

        return {
            clear: function () {
                input.value = '';
                hidden.value = '';
                clearSuggestions();
                requestCounter++;
            }
        };
    }

    if (!manualLinkForm) {
        return;
    }

    const manualLinkGame = document.getElementById('manualLinkGameType');
    const manualLinkUserSearch = document.getElementById('manualLinkUserSearch');
    const manualLinkUserProfileId = document.getElementById('manualLinkUserProfileId');
    const manualLinkPlayerSearch = document.getElementById('manualLinkPlayerSearch');
    const manualLinkPlayerId = document.getElementById('manualLinkPlayerId');
    const manualLinkCreateBtn = document.getElementById('manualLinkCreateBtn');
    const previewEl = document.getElementById('manualLinkPreview');
    const previewStatusEl = document.getElementById('manualLinkPreviewStatus');

    function setStatus(message, type) {
        if (!previewStatusEl) {
            return;
        }

        previewStatusEl.classList.remove('d-none', 'alert-info', 'alert-warning', 'alert-success', 'alert-danger');
        previewStatusEl.classList.add(type || 'alert-info');
        previewStatusEl.textContent = message;
    }

    function clearStatus() {
        if (!previewStatusEl) {
            return;
        }

        previewStatusEl.classList.add('d-none');
        previewStatusEl.textContent = '';
        previewStatusEl.classList.remove('alert-info', 'alert-warning', 'alert-success', 'alert-danger');
    }

    function setPreviewField(field, value) {
        const el = previewEl ? previewEl.querySelector('[data-field="' + field + '"]') : null;
        if (el) {
            el.textContent = value || '-';
        }
    }

    function setCreateEnabled(enabled) {
        if (!manualLinkCreateBtn) {
            return;
        }

        manualLinkCreateBtn.disabled = !enabled;
    }

    function clearPreview() {
        if (!previewEl) {
            return;
        }

        setPreviewField('user-display-name', '-');
        setPreviewField('user-email', '-');
        setPreviewField('user-forum-id', '-');
        setPreviewField('user-profile-id', '-');
        setPreviewField('player-username', '-');
        setPreviewField('player-guid', '-');
        setPreviewField('player-ip', '-');
        setPreviewField('player-game-type', '-');
        setPreviewField('player-last-seen', '-');
        setPreviewField('player-id', '-');
        previewEl.classList.add('d-none');
        setCreateEnabled(false);
    }

    async function loadPreview() {
        if (!manualLinkPlayerId?.value || !manualLinkUserProfileId?.value) {
            clearPreview();
            return;
        }

        const query = new URLSearchParams({
            playerId: manualLinkPlayerId.value,
            userProfileId: manualLinkUserProfileId.value
        });

        try {
            const response = await fetch('/ConnectedPlayers/GetManualLinkPreview?' + query.toString(), {
                method: 'GET',
                credentials: 'same-origin'
            });

            if (!response.ok) {
                const payload = await response.json().catch(function () { return null; });
                clearPreview();
                setStatus(payload?.message || 'Unable to load preview for the selected values.', 'alert-danger');
                return;
            }

            const payload = await response.json();
            const player = payload.player || {};
            const user = payload.userProfile || {};
            const checks = payload.checks || {};

            setPreviewField('user-display-name', user.displayName);
            setPreviewField('user-email', user.email);
            setPreviewField('user-forum-id', user.forumId);
            setPreviewField('user-profile-id', user.userProfileId);
            setPreviewField('player-username', player.username);
            setPreviewField('player-guid', player.guid);
            setPreviewField('player-ip', player.ipAddress);
            setPreviewField('player-game-type', player.gameType);
            setPreviewField('player-last-seen', player.lastSeen ? portalDate.formatDateTime(player.lastSeen) : '-');
            setPreviewField('player-id', player.playerId);

            previewEl.classList.remove('d-none');
            setCreateEnabled(!!checks.canLink);

            if (checks.canLink) {
                setStatus(checks.message || 'Ready to create manual link.', 'alert-success');
            } else {
                setStatus(checks.message || 'Selected player/profile cannot be linked.', 'alert-warning');
            }
        } catch {
            clearPreview();
            setStatus('Unable to load preview for the selected values.', 'alert-danger');
        }
    }

    const profileAutocomplete = initAutocomplete({
        inputSelector: '#manualLinkUserSearch',
        hiddenSelector: '#manualLinkUserProfileId',
        searchUrl: '/ConnectedPlayers/SearchUserProfiles',
        mapResultText: function (item) {
            const name = item.displayName || item.text || 'Unknown';
            const forumId = item.forumId ? ' (Forum: ' + item.forumId + ')' : '';
            return name + forumId;
        },
        onSelect: function () {
            loadPreview();
        }
    });

    const playerAutocomplete = initAutocomplete({
        inputSelector: '#manualLinkPlayerSearch',
        hiddenSelector: '#manualLinkPlayerId',
        searchUrl: '/ConnectedPlayers/SearchPlayers',
        minLength: 3,
        mapResultText: function (item) {
            const username = item.username || item.text || 'Unknown';
            const guidText = item.guid ? ' [' + item.guid + ']' : '';
            return username + guidText;
        },
        queryParams: function () {
            const gameType = manualLinkGame ? manualLinkGame.value : '';
            return gameType ? { gameType: gameType } : {};
        },
        mapEmptyMessage: function () {
            if (!manualLinkGame || !manualLinkGame.value) {
                return 'Select a game before searching players.';
            }

            return 'No players found.';
        },
        onSelect: function () {
            loadPreview();
        }
    });

    if (manualLinkGame) {
        manualLinkGame.addEventListener('change', function () {
            if (playerAutocomplete) {
                playerAutocomplete.clear();
            }

            clearPreview();
            clearStatus();
            if (manualLinkUserProfileId?.value) {
                setStatus('Select a player for the selected game to continue.', 'alert-info');
            }
        });
    }

    if (manualLinkUserSearch) {
        manualLinkUserSearch.addEventListener('input', function () {
            clearPreview();
            clearStatus();
        });
    }

    if (manualLinkPlayerSearch) {
        manualLinkPlayerSearch.addEventListener('input', function () {
            clearPreview();
            clearStatus();
        });
    }

    manualLinkForm.addEventListener('submit', async function (event) {
        event.preventDefault();

        if (!manualLinkPlayerId?.value || !manualLinkUserProfileId?.value) {
            setStatus('Select both a website profile and a player before creating a link.', 'alert-warning');
            return;
        }

        const token = getAntiForgeryToken();
        const submitText = manualLinkCreateBtn ? manualLinkCreateBtn.innerHTML : '';
        if (manualLinkCreateBtn) {
            manualLinkCreateBtn.disabled = true;
            manualLinkCreateBtn.innerHTML = '<i class="fa-solid fa-fw fa-spinner fa-spin" aria-hidden="true"></i> Creating...';
        }

        try {
            const response = await fetch('/ConnectedPlayers/CreateManualLinkAjax', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    playerId: manualLinkPlayerId.value,
                    userProfileId: manualLinkUserProfileId.value
                })
            });

            const payload = await response.json().catch(function () { return null; });
            if (!response.ok || !payload || payload.success !== true) {
                const message = payload?.message || 'Failed to create connected player link.';
                setStatus(message, 'alert-danger');
                showToastError(message);
                return;
            }

            const message = payload.message || 'Connected player link created successfully.';
            setStatus(message, 'alert-success');
            showToastSuccess(message);

            if (manualLinkGame) {
                manualLinkGame.value = '';
            }

            if (profileAutocomplete) {
                profileAutocomplete.clear();
            }

            if (playerAutocomplete) {
                playerAutocomplete.clear();
            }

            clearPreview();
            table.ajax.reload(null, false);
        } catch {
            const message = 'Unexpected error while creating connected player link.';
            setStatus(message, 'alert-danger');
            showToastError(message);
        } finally {
            if (manualLinkCreateBtn) {
                manualLinkCreateBtn.innerHTML = submitText;
                manualLinkCreateBtn.disabled = false;
                if (!manualLinkPlayerId?.value || !manualLinkUserProfileId?.value) {
                    manualLinkCreateBtn.disabled = true;
                }
            }
        }
    });

    clearPreview();
});
