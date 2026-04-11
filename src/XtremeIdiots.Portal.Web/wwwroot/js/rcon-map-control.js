/**
 * Map Control module — map rotation carousel and server control actions (restart, next map, load map).
 * Lifecycle: init(options) → start() → stop() → dispose()
 */
var RconMapControl = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _lastRefreshTime = new Date();
    var _selectors = {};
    var _onMapChanged = null;

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _onMapChanged = options.onMapChanged || null;
        _selectors = {
            carousel: options.carouselSelector || '#mapRotationCarousel',
            container: options.containerSelector || '#mapRotationContainer',
            loading: options.loadingSelector || '#mapRotationLoading',
            error: options.errorSelector || '#mapRotationError',
            refreshBadge: options.refreshBadgeId || 'mapRotationRefresh',
            restartMap: options.restartMapSelector || '#restartMap',
            fastRestartMap: options.fastRestartMapSelector || '#fastRestartMap',
            nextMap: options.nextMapSelector || '#nextMap',
            restartServer: options.restartServerSelector || '#restartServer'
        };
    }

    function loadRotation() {
        $.ajax({
            type: 'GET',
            url: '/ServerAdmin/GetMapRotation/' + _serverId,
            success: function (result) {
                if (result.success && result.maps && result.maps.length > 0) {
                    var carousel = $(_selectors.carousel);
                    carousel.empty();

                    result.maps.forEach(function (map) {
                        var mapCard = $('<div>', { class: 'map-card' });
                        var imgSrc = map.hasImage && map.mapImageUri ? map.mapImageUri : '/images/noimage.jpg';
                        var mapImg = $('<img>', {
                            src: imgSrc,
                            class: 'map-card-image',
                            alt: map.mapTitle,
                            onerror: "this.src='/images/noimage.jpg'"
                        });
                        var cardBody = $('<div>', { class: 'map-card-body' });
                        var mapTitle = $('<div>', { class: 'map-card-title', title: map.mapTitle }).text(map.mapTitle);
                        var loadBtn = $('<button>', {
                            class: 'btn btn-sm btn-primary map-card-btn load-map-btn',
                            'data-map-name': map.mapName
                        }).html('<i class="fa-solid fa-play"></i> Load Map');

                        cardBody.append(mapTitle).append(loadBtn);
                        mapCard.append(mapImg).append(cardBody);
                        carousel.append(mapCard);
                    });

                    $(_selectors.loading).hide();
                    $(_selectors.container).show();
                    _lastRefreshTime = new Date();
                    RconUtils.updateRefreshBadge(_selectors.refreshBadge, _lastRefreshTime);
                } else {
                    $(_selectors.loading).hide();
                    $(_selectors.error).text('No maps in rotation').show();
                }
            },
            error: function () {
                $(_selectors.loading).hide();
                $(_selectors.error).text('Failed to load map rotation').show();
            }
        });
    }

    function _sendMapCommand(action, confirmMsg, successMsg) {
        if (!confirm(confirmMsg)) return;
        $.ajax({
            type: 'POST',
            url: '/ServerAdmin/' + action + '/' + _serverId,
            data: { __RequestVerificationToken: _antiForgeryToken },
            success: function () {
                RconUtils.showToast('success', successMsg);
                if (_onMapChanged) setTimeout(_onMapChanged, 2000);
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Failed: ' + (xhr.responseText || 'Unknown error'));
            }
        });
    }

    function bindActions() {
        $(_selectors.restartMap).off('click').on('click', function (e) {
            e.preventDefault();
            _sendMapCommand('RestartMap', 'Are you sure you want to restart the map?', 'Map restart command sent successfully');
        });

        $(_selectors.fastRestartMap).off('click').on('click', function (e) {
            e.preventDefault();
            _sendMapCommand('FastRestartMap', 'Are you sure you want to fast restart the map?', 'Fast restart command sent successfully');
        });

        $(_selectors.nextMap).off('click').on('click', function (e) {
            e.preventDefault();
            _sendMapCommand('NextMap', 'Are you sure you want to load the next map?', 'Next map command sent successfully');
        });

        $(_selectors.restartServer).off('click').on('click', function (e) {
            e.preventDefault();
            _sendMapCommand('RestartServer', 'Are you sure you want to restart the server?', 'Server restart command sent successfully');
        });

        // Load map button (delegated)
        $(document).off('click', '.load-map-btn').on('click', '.load-map-btn', function (e) {
            e.preventDefault();
            var mapName = $(this).data('map-name');
            if (!confirm('Load map "' + mapName + '"?')) return;

            $.ajax({
                type: 'POST',
                url: '/ServerAdmin/LoadMap/' + _serverId,
                data: {
                    mapName: mapName,
                    __RequestVerificationToken: _antiForgeryToken
                },
                success: function (result) {
                    if (result.success) {
                        RconUtils.showToast('success', result.message || 'Map load command sent');
                        if (_onMapChanged) setTimeout(_onMapChanged, 2000);
                    } else {
                        RconUtils.showToast('error', result.message || 'Failed to load map');
                    }
                },
                error: function (xhr) {
                    RconUtils.showToast('error', 'Error loading map: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
                }
            });
        });
    }

    function start() {
        loadRotation();
        bindActions();
    }

    function stop() {
        // No polling for map rotation
    }

    function dispose() {
        stop();
        // Unbind delegated handler
        $(document).off('click', '.load-map-btn');
        // Unbind button handlers
        if (_selectors.restartMap) $(_selectors.restartMap).off('click');
        if (_selectors.fastRestartMap) $(_selectors.fastRestartMap).off('click');
        if (_selectors.nextMap) $(_selectors.nextMap).off('click');
        if (_selectors.restartServer) $(_selectors.restartServer).off('click');
        _serverId = null;
    }

    return {
        init: init,
        loadRotation: loadRotation,
        start: start,
        stop: stop,
        dispose: dispose
    };
})();
