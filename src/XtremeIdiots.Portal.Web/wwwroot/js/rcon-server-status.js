/**
 * Server Status module — polls server current map, player count, and online status.
 * Lifecycle: init(options) → start() → stop() → dispose()
 */
var RconServerStatus = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _intervalId = null;
    var _refreshEnabled = true;
    var _lastRefreshTime = new Date();
    var _selectors = {};
    var _onUpdated = null;

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _onUpdated = options.onUpdated || null;
        _selectors = {
            mapName: options.mapNameSelector || '#currentMapName',
            mapImage: options.mapImageSelector || '#currentMapImage',
            playerCount: options.playerCountSelector || '#playerCount',
            maxPlayers: options.maxPlayersSelector || '#maxPlayers',
            statusIndicator: options.statusIndicatorSelector || '#serverStatusIndicator',
            refreshBadge: options.refreshBadgeId || 'serverStatusRefresh',
            loadingIndicator: options.loadingIndicatorSelector || '#serverInfoLoading',
            contentContainer: options.contentContainerSelector || '#serverInfoContent'
        };
    }

    function load(callback) {
        if (!_refreshEnabled || !_serverId) return;

        $(_selectors.loadingIndicator).show();
        $(_selectors.contentContainer).css('opacity', '0.5');

        $.ajax({
            url: '/ServerAdmin/GetCurrentMap/' + _serverId,
            type: 'GET',
            success: function (result) {
                if (result.success) {
                    $(_selectors.mapName).text(result.currentMap || 'Unknown');
                    $(_selectors.mapImage).attr('src', result.mapImageUri || '/images/noimage.jpg');
                    _lastRefreshTime = new Date();
                    RconUtils.updateRefreshBadge(_selectors.refreshBadge, _lastRefreshTime);
                }
                $(_selectors.loadingIndicator).hide();
                $(_selectors.contentContainer).css('opacity', '1');
                if (typeof _onUpdated === 'function') _onUpdated();
                if (typeof callback === 'function') callback();
            },
            error: function () {
                $(_selectors.statusIndicator).removeClass('bg-success').addClass('bg-danger').text('Error');
                $(_selectors.loadingIndicator).hide();
                $(_selectors.contentContainer).css('opacity', '1');
                if (typeof callback === 'function') callback();
            }
        });
    }

    function loadServerInfo() {
        $.ajax({
            url: '/ServerAdmin/GetServerInfo/' + _serverId,
            type: 'GET',
            success: function (result) {
                if (result.success && result.serverInfo) {
                    RconUtils.showInfoModal('Server Information', result.serverInfo);
                } else {
                    RconUtils.showToast('error', result.message || 'Failed to load server info');
                }
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Failed to load server info: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
            }
        });
    }

    function loadSystemInfo() {
        $.ajax({
            url: '/ServerAdmin/GetSystemInfo/' + _serverId,
            type: 'GET',
            success: function (result) {
                if (result.success && result.systemInfo) {
                    RconUtils.showInfoModal('System Information', result.systemInfo);
                } else {
                    RconUtils.showToast('error', result.message || 'Failed to load system info');
                }
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Failed to load system info: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
            }
        });
    }

    function loadCommandList() {
        $.ajax({
            url: '/ServerAdmin/GetCommandList/' + _serverId,
            type: 'GET',
            success: function (result) {
                if (result.success && result.commandList) {
                    RconUtils.showInfoModal('Command List', result.commandList);
                } else {
                    RconUtils.showToast('error', result.message || 'Failed to load command list');
                }
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Failed to load command list: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
            }
        });
    }

    function setRefreshEnabled(enabled) {
        _refreshEnabled = enabled;
    }

    function start(intervalMs) {
        load();
        if (_intervalId) clearInterval(_intervalId);
        _intervalId = setInterval(function () {
            if (_refreshEnabled) load();
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
        _serverId = null;
    }

    return {
        init: init,
        load: load,
        loadServerInfo: loadServerInfo,
        loadSystemInfo: loadSystemInfo,
        loadCommandList: loadCommandList,
        setRefreshEnabled: setRefreshEnabled,
        start: start,
        stop: stop,
        dispose: dispose
    };
})();
