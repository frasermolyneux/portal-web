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

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _tableSelector = options.tableSelector || '#dataTable';
        _refreshBadgeId = options.refreshBadgeId || 'playersTableRefresh';
        _playerCountSelector = options.playerCountSelector || '#playerCount';
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
            ajax: {
                url: '/ServerAdmin/GetRconPlayers/' + _serverId,
                dataSrc: function (json) {
                    _lastRefreshTime = new Date();
                    RconUtils.updateRefreshBadge(_refreshBadgeId, _lastRefreshTime);
                    if (json.data) {
                        $(_playerCountSelector).text(json.data.length || 0);
                    }
                    return json.data;
                },
                error: RconUtils.handleAjaxError
            },
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
                        if (row.playerId) {
                            return '<a href="/Players/Details/' + row.playerId + '">' + CodColors.renderSafe(row.name) + '</a>';
                        }
                        return CodColors.renderSafe(row.name);
                    }
                },
                { data: 'guid', name: 'guid', sortable: false },
                {
                    data: 'ipAddress', name: 'ipAddress', sortable: false,
                    render: function (data, type, row) {
                        if (!data) return '';
                        return formatIPAddress(
                            row.ipAddress,
                            row.proxyCheckRiskScore || 0,
                            row.isProxy === true,
                            row.isVpn === true,
                            row.proxyType || '',
                            row.countryCode || '',
                            true
                        );
                    }
                },
                { data: 'rate', name: 'rate', sortable: false },
                {
                    data: null, sortable: false,
                    render: function (data, type, row) {
                        if (row.num == null) return '';
                        var s = row.num, g = row.guid || '', n = escapeHtml(row.name || 'Unknown');
                        return '<div class="btn-group btn-group-sm" role="group">' +
                            '<button class="btn btn-sm btn-warning kick-player" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-user-xmark"></i> Kick</button> ' +
                            '<button class="btn btn-sm btn-danger tempban-player" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-clock"></i> TempBan</button> ' +
                            '<button class="btn btn-sm btn-danger ban-player" data-slot="' + s + '" data-guid="' + g + '" data-name="' + n + '"><i class="fa-solid fa-ban"></i> Ban</button>' +
                            '</div>';
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
                    if (_table) _table.ajax.reload();
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
        if (_table) _table.ajax.reload(null, false);
    }

    function setRefreshEnabled(enabled) {
        _refreshEnabled = enabled;
    }

    function start(intervalMs) {
        initTable();
        if (_intervalId) clearInterval(_intervalId);
        _intervalId = setInterval(function () {
            if (_refreshEnabled) reload();
        }, intervalMs || 10000);
    }

    function stop() {
        if (_intervalId) {
            clearInterval(_intervalId);
            _intervalId = null;
        }
    }

    function dispose() {
        stop();
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
