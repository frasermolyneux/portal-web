/**
 * Unified server feed module for chat and server events on Server Detail overview.
 * Lifecycle: init(options) -> start(intervalMs) -> stop() -> dispose()
 */
var ServerFeed = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _intervalId = null;
    var _refreshEnabled = true;
    var _isBackgrounded = false;
    var _items = [];
    var _seenItemIds = new Set();
    var _pendingItems = [];
    var _overrunCount = 0;
    var _overrunNoticeShown = false;

    var _cursor = {
        lastSeenTimestampUtc: null,
        lastSeenSourceType: null,
        lastSeenItemId: null,
        lastChatMessageId: null,
        lastEventId: null
    };

    var _selectors = {};

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _refreshEnabled = true;
        _isBackgrounded = false;
        _items = [];
        _pendingItems = [];
        _seenItemIds = new Set();
        _overrunCount = 0;
        _overrunNoticeShown = false;
        _cursor = {
            lastSeenTimestampUtc: null,
            lastSeenSourceType: null,
            lastSeenItemId: null,
            lastChatMessageId: null,
            lastEventId: null
        };

        _selectors = {
            container: options.containerSelector || '#sd-feedContainer',
            items: options.itemsSelector || '#sd-feedItems',
            pendingCount: options.pendingCountSelector || '#sd-feedPendingCount',
            overrunIndicator: options.overrunIndicatorSelector || null,
            chatToggle: options.chatToggleSelector || '#sd-feedToggleChat',
            eventsToggle: options.eventsToggleSelector || '#sd-feedToggleEvents',
            eventTypeFilter: options.eventTypeFilterSelector || '#sd-feedEventFilter',
            sayForm: options.sayFormSelector || '#sd-sayForm',
            sayInput: options.sayInputSelector || '#sd-sayMessage'
        };

        bindUiEvents();
    }

    function bindUiEvents() {
        $(_selectors.chatToggle).off('change.serverFeed').on('change.serverFeed', function () {
            forceReload();
        });

        $(_selectors.eventsToggle).off('change.serverFeed').on('change.serverFeed', function () {
            forceReload();
        });

        $(_selectors.eventTypeFilter).off('input.serverFeed').on('input.serverFeed', function () {
            render();
        });

        document.removeEventListener('visibilitychange', onVisibilityChanged);
        document.addEventListener('visibilitychange', onVisibilityChanged);

        bindSayForm();
    }

    function onVisibilityChanged() {
        _isBackgrounded = document.hidden === true;
    }

    function bindSayForm() {
        if (!_selectors.sayForm) {
            return;
        }

        $(_selectors.sayForm).off('submit.serverFeed').on('submit.serverFeed', function (e) {
            e.preventDefault();

            var message = ($(_selectors.sayInput).val() || '').trim();
            if (!message) {
                RconUtils.showToast('warning', 'Please enter a message');
                return;
            }

            $.ajax({
                type: 'POST',
                url: '/ServerAdmin/SendSayCommand/' + _serverId,
                data: {
                    message: message,
                    __RequestVerificationToken: _antiForgeryToken
                },
                success: function (result) {
                    if (result.success) {
                        RconUtils.showToast('success', result.message || 'Message sent successfully');
                        $(_selectors.sayInput).val('');
                        forceReload();
                        return;
                    }

                    RconUtils.showToast('error', result.message || 'Failed to send message');
                },
                error: function (xhr) {
                    RconUtils.showToast('error', 'Error sending message: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
                }
            });
        });
    }

    function readSourceToggles() {
        return {
            includeChat: $(_selectors.chatToggle).is(':checked'),
            includeEvents: $(_selectors.eventsToggle).is(':checked')
        };
    }

    function buildFeedUrl() {
        var toggles = readSourceToggles();
        var query = [];

        query.push('minutes=30');
        query.push('maxItems=100');
        query.push('includeChat=' + (toggles.includeChat ? 'true' : 'false'));
        query.push('includeEvents=' + (toggles.includeEvents ? 'true' : 'false'));

        if (_cursor.lastSeenTimestampUtc && _cursor.lastSeenSourceType && _cursor.lastSeenItemId) {
            query.push('lastSeenTimestampUtc=' + encodeURIComponent(_cursor.lastSeenTimestampUtc));
            query.push('lastSeenSourceType=' + encodeURIComponent(_cursor.lastSeenSourceType));
            query.push('lastSeenItemId=' + encodeURIComponent(_cursor.lastSeenItemId));
        }

        if (_cursor.lastChatMessageId) {
            query.push('lastChatMessageId=' + encodeURIComponent(_cursor.lastChatMessageId));
        }

        if (_cursor.lastEventId) {
            query.push('lastEventId=' + encodeURIComponent(_cursor.lastEventId));
        }

        return '/ServerAdmin/GetServerFeed/' + _serverId + '?' + query.join('&');
    }

    function load() {
        if (!_serverId || _isBackgrounded) {
            return;
        }

        $.ajax({
            url: buildFeedUrl(),
            type: 'GET',
            success: function (result) {
                var items = Array.isArray(result?.items) ? result.items : [];

                if (result?.cursor) {
                    _cursor.lastSeenTimestampUtc = result.cursor.lastSeenTimestampUtc;
                    _cursor.lastSeenSourceType = result.cursor.lastSeenSourceType;
                    _cursor.lastSeenItemId = result.cursor.lastSeenItemId;
                    _cursor.lastChatMessageId = result.cursor.lastChatMessageId;
                    _cursor.lastEventId = result.cursor.lastEventId;
                }

                if (result?.diagnostics?.overrunDetected === true) {
                    _overrunCount += 1;
                    if (_selectors.overrunIndicator) {
                        $(_selectors.overrunIndicator).show();
                    }
                    if (!_overrunNoticeShown) {
                        _overrunNoticeShown = true;
                        prependSystemNotice('Feed volume is high \u2014 some items may be truncated.');
                    }
                } else {
                    _overrunCount = 0;
                    if (_selectors.overrunIndicator) {
                        $(_selectors.overrunIndicator).hide();
                    }
                }

                var dedupedItems = [];
                for (var i = 0; i < items.length; i++) {
                    var item = items[i];
                    if (!item || !item.itemId) {
                        continue;
                    }

                    if (_seenItemIds.has(item.itemId)) {
                        continue;
                    }

                    _seenItemIds.add(item.itemId);
                    dedupedItems.push(item);
                }

                if (dedupedItems.length === 0) {
                    render();
                    return;
                }

                if (!_refreshEnabled) {
                    _pendingItems = dedupedItems.concat(_pendingItems);
                    updatePendingCount();
                    return;
                }

                _items = dedupedItems.concat(_items);
                trimFeed();
                render();
                scrollToTop();
            },
            error: function (xhr) {
                console.warn('Failed to load server feed:', xhr?.status, xhr?.responseText);
            }
        });
    }

    function prependSystemNotice(message) {
        _items.unshift({
            itemId: 'system:' + Date.now().toString(),
            sourceType: 'system',
            timestampUtc: new Date().toISOString(),
            displayText: message,
            username: null,
            playerId: null,
            eventType: null,
            rawEventData: null,
            locked: false
        });

        trimFeed();
        render();
    }

    function trimFeed() {
        var maxRetained = 400;
        if (_items.length > maxRetained) {
            _items = _items.slice(0, maxRetained);
            _seenItemIds = new Set(_items.map(function (x) { return x.itemId; }));
        }
    }

    function updatePendingCount() {
        var pendingCountEl = $(_selectors.pendingCount);

        if (_pendingItems.length > 0) {
            pendingCountEl.text(_pendingItems.length + ' new').show();
            return;
        }

        pendingCountEl.hide().text('0 new');
    }

    function forceReload(loadImmediately) {
        _cursor.lastSeenTimestampUtc = null;
        _cursor.lastSeenSourceType = null;
        _cursor.lastSeenItemId = null;
        _cursor.lastChatMessageId = null;
        _cursor.lastEventId = null;
        _items = [];
        _pendingItems = [];
        _seenItemIds = new Set();
        updatePendingCount();
        if (loadImmediately !== false) {
            load();
        }
    }

    function escapeHtml(value) {
        if (value === null || value === undefined) {
            return '';
        }

        var div = document.createElement('div');
        div.textContent = String(value);
        return div.innerHTML;
    }

    function renderEventData(rawEventData) {
        if (!rawEventData) {
            return '';
        }

        try {
            var parsed = JSON.parse(rawEventData);
            var keys = Object.keys(parsed);
            if (keys.length === 0) {
                return '';
            }

            var rows = [];
            for (var i = 0; i < keys.length; i++) {
                var key = keys[i];
                rows.push('<small><strong>' + escapeHtml(key) + ':</strong> ' + escapeHtml(parsed[key]) + '</small>');
            }

            return '<details><summary class="text-muted"><small>' + keys.length + ' fields</small></summary><div class="mt-1">' + rows.join('<br>') + '</div></details>';
        } catch (_err) {
            return '<small class="text-muted">' + escapeHtml(rawEventData) + '</small>';
        }
    }

    function renderItem(item) {
        if (item.sourceType === 'chat') {
            var username = item.username ? CodColors.renderSafe(item.username) : 'Unknown';
            var message = CodColors.renderSafe(item.displayText || '');
            var playerLink = item.playerId
                ? '<a href="/Players/Details/' + item.playerId + '" class="alert-link">' + username + '</a>'
                : username;
            var lockedBadge = item.locked ? '<span class="badge bg-warning text-dark ms-2">Locked</span>' : '';

            return '' +
                '<div class="alert alert-info alert-sm mb-1" data-source="chat">' +
                '<small class="text-muted" title="' + portalDate.formatDateTime(item.timestampUtc) + '">' + portalDate.formatDateTime(item.timestampUtc, { showRelative: true }) + '</small> ' +
                '<span class="badge bg-primary-subtle text-primary-emphasis ms-1">Chat</span> ' +
                '<strong>' + playerLink + '</strong>' +
                lockedBadge +
                ': ' + message +
                '</div>';
        }

        if (item.sourceType === 'event') {
            var eventType = item.eventType || item.displayText || 'Event';
            var serverName = item.username ? '<small class="text-muted ms-1">' + escapeHtml(item.username) + '</small>' : '';
            return '' +
                '<div class="alert alert-secondary alert-sm mb-1" data-source="event" data-event-type="' + escapeHtml(eventType).toLowerCase() + '">' +
                '<small class="text-muted" title="' + portalDate.formatDateTime(item.timestampUtc) + '">' + portalDate.formatDateTime(item.timestampUtc, { showRelative: true }) + '</small> ' +
                '<span class="badge bg-secondary-subtle text-secondary-emphasis ms-1">Event</span> ' +
                '<strong><code>' + escapeHtml(eventType) + '</code></strong>' +
                serverName +
                '<div class="mt-1">' + renderEventData(item.rawEventData) + '</div>' +
                '</div>';
        }

        return '' +
            '<div class="alert alert-warning alert-sm mb-1" data-source="system">' +
            '<small class="text-muted" title="' + portalDate.formatDateTime(item.timestampUtc) + '">' + portalDate.formatDateTime(item.timestampUtc, { showRelative: true }) + '</small> ' +
            '<span class="badge bg-warning text-dark ms-1">Notice</span> ' +
            escapeHtml(item.displayText || 'System message') +
            '</div>';
    }

    function render() {
        var toggles = readSourceToggles();
        var eventTypeFilter = ($(_selectors.eventTypeFilter).val() || '').trim().toLowerCase();

        var filteredItems = _items.filter(function (item) {
            if (item.sourceType === 'chat' && !toggles.includeChat) {
                return false;
            }

            if (item.sourceType === 'event') {
                if (!toggles.includeEvents) {
                    return false;
                }

                if (eventTypeFilter.length > 0) {
                    var eventType = (item.eventType || item.displayText || '').toLowerCase();
                    if (eventType.indexOf(eventTypeFilter) === -1) {
                        return false;
                    }
                }
            }

            return true;
        });

        if (filteredItems.length === 0) {
            $(_selectors.items).html('<p class="text-muted text-center mb-0">No matching feed items</p>');
            return;
        }

        var html = '';
        for (var i = 0; i < filteredItems.length; i++) {
            html += renderItem(filteredItems[i]);
        }

        $(_selectors.items).html(html);
    }

    function scrollToTop() {
        $(_selectors.container).scrollTop(0);
    }

    function setRefreshEnabled(enabled) {
        var wasPaused = !_refreshEnabled;
        _refreshEnabled = enabled;

        if (!enabled) {
            return;
        }

        if (wasPaused && _pendingItems.length > 0) {
            _items = _pendingItems.concat(_items);
            _pendingItems = [];
            trimFeed();
            updatePendingCount();
            render();
            scrollToTop();
        }
    }

    function start(intervalMs) {
        load();
        if (_intervalId) {
            clearInterval(_intervalId);
        }

        _intervalId = setInterval(load, intervalMs || 15000);
    }

    function stop() {
        if (_intervalId) {
            clearInterval(_intervalId);
            _intervalId = null;
        }
    }

    function dispose() {
        stop();

        if (_selectors.sayForm) {
            $(_selectors.sayForm).off('submit.serverFeed');
        }

        $(_selectors.chatToggle).off('change.serverFeed');
        $(_selectors.eventsToggle).off('change.serverFeed');
        $(_selectors.eventTypeFilter).off('input.serverFeed');

        document.removeEventListener('visibilitychange', onVisibilityChanged);

        _items = [];
        _pendingItems = [];
        _seenItemIds = new Set();
        _serverId = null;
    }

    return {
        init: init,
        start: start,
        stop: stop,
        setRefreshEnabled: setRefreshEnabled,
        dispose: dispose,
        forceReload: forceReload
    };
})();
