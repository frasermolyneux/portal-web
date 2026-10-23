/**
 * Server Status module — polls server current map, player count, and online status.
 * Lifecycle: init(options) → start() → stop() → dispose()
 */
var RconServerStatus = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _poller = null;
    var _refreshEnabled = true;
    var _lastRefreshTime = new Date();
    var _selectors = {};
    var _onUpdated = null;

    function init(options) {
        if (_poller) _poller.dispose();
        _poller = null;
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
        ensurePoller();
        return _poller.refresh().then(function () {
            if (typeof callback === 'function') callback();
        });
    }

    function ensurePoller(intervalMs) {
        if (_poller) return;
        _poller = PortalAsync.createPoller({
            intervalMs: intervalMs || 10000,
            load: async function (signal) {
                if (!_refreshEnabled || !_serverId) return;
                $(_selectors.loadingIndicator).show();
                $(_selectors.contentContainer).css('opacity', '0.5');
                try {
                    var response = await fetch('/ServerAdmin/GetCurrentMap/' + _serverId, {
                        credentials: 'same-origin', signal: signal
                    });
                    if (!response.ok || response.redirected) throw new Error('Server status unavailable (' + response.status + ')');
                    var result = await response.json();
                    if (signal.aborted) return;
                    if (!result.success) throw new Error(result.message || 'Server status unavailable');
                    $(_selectors.mapName).text(result.currentMap || 'Unknown');
                    $(_selectors.mapImage).attr('src', result.mapImageUri || '/images/noimage.jpg');
                    _lastRefreshTime = new Date();
                    RconUtils.updateRefreshBadge(_selectors.refreshBadge, _lastRefreshTime);
                    if (typeof _onUpdated === 'function') _onUpdated();
                } finally {
                    $(_selectors.loadingIndicator).hide();
                    $(_selectors.contentContainer).css('opacity', '1');
                }
            },
            onError: function (error) {
                console.warn('Server status unavailable:', error);
                $(_selectors.statusIndicator).removeClass('bg-success').addClass('bg-danger').text('Error');
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
        ensurePoller(intervalMs);
        _poller.start();
    }

    function stop() {
        if (_poller) _poller.stop();
    }

    function dispose() {
        if (_poller) _poller.dispose();
        _poller = null;
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
