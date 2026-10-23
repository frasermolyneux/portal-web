/**
 * Connected Players module — DataTable of live RCON players with kick/tempban/ban actions.
 * Lifecycle: init(options) → start() → stop() → dispose()
 */
var RconPlayers = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _intervalId = null;
    var _refreshEnabled = true;
    var _lastRefreshTime = new Date();
    var _table = null;
    var _tableSelector = null;
    var _refreshBadgeId = null;
    var _playerCountSelector = null;
    var _canKick = false;
    var _canTempBan = false;
    var _canBan = false;
    var _liveRequest = null;
    var _running = false;
    var _disposed = false;
    var _intervalMs = 10000;
    var _enrichmentCache = new Map();
    var _pendingEnrichment = new Map();

    function init(options) {
        _disposed = false;
        _enrichmentCache.clear();
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _tableSelector = options.tableSelector || '#dataTable';
        _refreshBadgeId = options.refreshBadgeId || 'playersTableRefresh';
        _playerCountSelector = options.playerCountSelector || '#playerCount';
        _canKick = options.canKick === true;
        _canTempBan = options.canTempBan === true;
        _canBan = options.canBan === true;
    }

    function initTable() {
        if (_table) return _table;

        _table = $(_tableSelector).DataTable({
            processing: true,
            paging: false,
            info: false,
            searching: false,
            stateSave: true,
            responsive: {
                details: { type: 'inline', target: 'tr' }
            },
            autoWidth: false,
            order: [[0, "asc"]],
            ajax: loadPlayers,
            columnDefs: [
                { targets: 0, responsivePriority: 1 },
                { targets: 1, responsivePriority: 2 },
                { targets: 2, responsivePriority: 5 },
                { targets: 3, responsivePriority: 4 },
                { targets: 4, responsivePriority: 6 },
                { targets: 5, responsivePriority: 3 }
            ],
            columns: [
                { data: 'num', name: 'num', width: '50px' },
                {
                    data: 'name', name: 'name',
                    render: function (data, type, row) {
                        if (type !== 'display') return data;
                        if (row.playerId) {
                            return '<a href="/Players/Details/' + row.playerId + '" target="_blank" rel="noopener noreferrer">' + CodColors.renderSafe(row.name) + '</a>';
                        }
                        var profileLabels = { unavailable: 'Profile unavailable', notFound: 'No profile', notApplicable: '' };
                        var profileLabel = profileLabels[row.profileStatus] ?? 'Profile pending';
                        return CodColors.renderSafe(row.name) +
                            (profileLabel ? ' <small class="text-muted">' + profileLabel + '</small>' : '');
                    }
                },
                { data: 'guid', name: 'guid', sortable: false },
                {
                    data: 'ipAddress', name: 'ipAddress', sortable: false,
                    render: function (data, type, row) {
                        if (!data) return '';
                        if (type !== 'display') return data;
                        var available = row.ipIntelligenceStatus === 'available';
                        var rendered = formatIPAddress(
                            row.ipAddress,
                            available ? row.proxyCheckRiskScore : null,
                            available && row.isProxy === true,
                            available && row.isVpn === true,
                            available ? row.proxyType || '' : '',
                            row.countryCode || '',
                            true
                        );
                        if (!available) {
                            rendered += ' <small class="text-muted">' +
                                (row.ipIntelligenceStatus === 'unavailable' ? 'Risk unavailable' : 'Risk pending') + '</small>';
                        }
                        return rendered;
                    }
                },
                { data: 'rate', name: 'rate', sortable: false },
                {
                    data: null, sortable: false,
                    render: function (data, type, row) {
                        if (row.num == null) return '';
                        var s = escapeHtml(String(row.num)), g = escapeHtml(String(row.guid || '')), n = escapeHtml(row.name || 'Unknown');
                        var buttons = '';
                        if (_canKick) {
                            buttons += '<button class="btn btn-sm btn-warning kick-player" data-testid="player-kick" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-fw fa-user-xmark" aria-hidden="true"></i> Kick</button> ';
                        }
                        if (_canTempBan) {
                            buttons += '<button class="btn btn-sm btn-danger tempban-player" data-testid="player-temp-ban" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-fw fa-clock" aria-hidden="true"></i> TempBan</button> ';
                        }
                        if (_canBan) {
                            buttons += '<button class="btn btn-sm btn-danger ban-player" data-testid="player-ban" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-fw fa-ban" aria-hidden="true"></i> Ban</button>';
                        }
                        return buttons ? '<div class="btn-group btn-group-sm" role="group">' + buttons + '</div>' : '';
                    }
                }
            ]
        });

        // Delegate action button clicks
        $(_tableSelector).on('click', '.kick-player', function () { _handlePlayerAction('KickRconPlayer', $(this), 'kick'); });
        $(_tableSelector).on('click', '.tempban-player', function () { _handlePlayerAction('TempBanRconPlayer', $(this), 'temporarily ban'); });
        $(_tableSelector).on('click', '.ban-player', function () { _handlePlayerAction('BanRconPlayer', $(this), 'permanently ban'); });

        return _table;
    }

    function playerKey(player) {
        return JSON.stringify([player.guid || '', player.ipAddress || '', player.guid ? null : player.num]);
    }

    function cachedEnrichment(key) {
        var cached = _enrichmentCache.get(key);
        if (cached && cached.expiresAt > Date.now()) return cached.value;
        _enrichmentCache.delete(key);
        return null;
    }

    function cacheEnrichment(key, value, expiresAt) {
        _enrichmentCache.delete(key);
        _enrichmentCache.set(key, { value: value, expiresAt: expiresAt });
        if (_enrichmentCache.size > 256) {
            _enrichmentCache.delete(_enrichmentCache.keys().next().value);
        }
    }

    function scheduleRefresh() {
        if (_intervalId) clearTimeout(_intervalId);
        if (!_running || _disposed) return;
        _intervalId = setTimeout(function () {
            _intervalId = null;
            if (_refreshEnabled) reload();
            else scheduleRefresh();
        }, _intervalMs);
    }

    function loadPlayers(request, callback) {
        if (_disposed || _liveRequest) return;
        if (_intervalId) clearTimeout(_intervalId);
        _liveRequest = $.ajax({
            url: '/ServerAdmin/GetRconPlayers/' + _serverId,
            type: 'GET',
            success: function (json) {
                if (_disposed) return;
                json.data = json.data.map(function (player) {
                    return { ...player, ...cachedEnrichment(playerKey(player)) };
                });
                _lastRefreshTime = new Date();
                RconUtils.updateRefreshBadge(_refreshBadgeId, _lastRefreshTime);
                $(_playerCountSelector).text(json.data.length);
                callback(json);
                // DataTables can invoke the initial AJAX callback before initTable returns.
                if (_table) enrichPlayers();
            },
            error: function (xhr, textStatus, errorThrown) {
                if (_disposed || textStatus === 'abort') return;
                RconUtils.handleAjaxError(xhr, textStatus, errorThrown);
                callback({ data: _table ? _table.rows().data().toArray() : [] });
            },
            complete: function () {
                _liveRequest = null;
                scheduleRefresh();
            }
        });
        return _liveRequest;
    }

    function applyEnrichment(key, value) {
        if (!_table || _disposed) return;
        var updated = false;
        _table.rows().every(function () {
            var player = this.data();
            if (playerKey(player) === key) {
                this.data({ ...player, ...value });
                updated = true;
            }
        });
        if (updated) _table.draw(false);
    }

    function enrichPlayers() {
        if (_disposed || !_table) return;
        _table.rows().data().toArray().some(function (player) {
            if (_pendingEnrichment.size >= 2) return true;
            var key = playerKey(player);
            if (cachedEnrichment(key) || _pendingEnrichment.has(key)) return false;
            if (!player.guid && !player.ipAddress) return false;

            _pendingEnrichment.set(key, null);
            var request = $.ajax({
                url: '/ServerAdmin/GetRconPlayerEnrichment/' + _serverId,
                type: 'GET',
                data: { playerSlot: player.num, playerGuid: player.guid, ipAddress: player.ipAddress },
                success: function (value) {
                    if (_disposed) return;
                    cacheEnrichment(key, value, Math.max(Date.now() + 1000, Date.parse(value.expiresAtUtc)));
                    applyEnrichment(key, value);
                },
                error: function (xhr, textStatus) {
                    if (_disposed || textStatus === 'abort') return;
                    console.warn('Player enrichment unavailable:', xhr.status);
                    var value = { playerId: null, profileStatus: 'unavailable', ipIntelligenceStatus: 'unavailable' };
                    cacheEnrichment(key, value, Date.now() + 30000);
                    applyEnrichment(key, value);
                },
                complete: function () {
                    _pendingEnrichment.delete(key);
                    enrichPlayers();
                }
            });
            _pendingEnrichment.set(key, request);
            return false;
        });
    }

    function _handlePlayerAction(action, $btn, verb) {
        var playerSlot = $btn.data('slot');
        var playerGuid = $btn.data('guid') || '';
        var playerName = $btn.data('name') || 'this player';

        if (!confirm('Are you sure you want to ' + verb + ' ' + playerName + '?')) return;

        $.ajax({
            type: 'POST',
            url: '/ServerAdmin/' + action + '/' + _serverId,
            data: {
                playerSlot: playerSlot,
                playerGuid: playerGuid,
                playerName: playerName,
                __RequestVerificationToken: _antiForgeryToken
            },
            success: function (result) {
                if (result.success) {
                    RconUtils.showToast(verb.includes('ban') ? 'warning' : 'success', result.message || 'Action completed');
                    reload();
                } else {
                    RconUtils.showToast('error', result.message || 'Action failed');
                }
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Error: ' + (xhr.responseText || 'Unknown error'));
            }
        });
    }

    function reload() {
        if (_table && !_liveRequest && !_disposed) _table.ajax.reload(null, false);
    }

    function setRefreshEnabled(enabled) {
        _refreshEnabled = enabled;
    }

    function start(intervalMs) {
        _running = true;
        _intervalMs = intervalMs || 10000;
        initTable();
        if (!_liveRequest) scheduleRefresh();
    }

    function stop() {
        _running = false;
        if (_intervalId) {
            clearTimeout(_intervalId);
            _intervalId = null;
        }
    }

    function dispose() {
        _disposed = true;
        stop();
        if (_liveRequest) _liveRequest.abort();
        _pendingEnrichment.forEach(function (request) { if (request) request.abort(); });
        _pendingEnrichment.clear();
        _enrichmentCache.clear();
        if (_tableSelector) {
            $(_tableSelector).off('click', '.kick-player');
            $(_tableSelector).off('click', '.tempban-player');
            $(_tableSelector).off('click', '.ban-player');
        }
        if (_table) {
            _table.destroy();
            _table = null;
        }
    }

    return {
        init: init,
        initTable: initTable,
        reload: reload,
        setRefreshEnabled: setRefreshEnabled,
        start: start,
        stop: stop,
        dispose: dispose
    };
})();
