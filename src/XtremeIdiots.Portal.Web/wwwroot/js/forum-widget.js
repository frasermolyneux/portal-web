/**
 * XtremeIdiots Portal — Forum Notification Widget
 * Self-contained script for embedding on xtremeidiots.com (Invision Community).
 *
 * Usage: Add to the forum template:
 *   <div id="portal-notifications-widget" data-token="{hmac_token}" data-portal-url="https://portal.xtremeidiots.com"></div>
 *   <script src="https://portal.xtremeidiots.com/js/forum-widget.js"></script>
 */
(function () {
    'use strict';

    var POLL_INTERVAL = 60000;
    var pollTimer = null;
    var widgetState = { token: null, portalUrl: '', authenticated: false, filters: {} };

    // Action type icons (Unicode)
    var ACTION_ICONS = {
        'Ban': '\u{1F6AB}',
        'TempBan': '\u23F1\uFE0F',
        'Kick': '\u{1F462}',
        'Warning': '\u26A0\uFE0F',
        'Observation': '\u{1F441}\uFE0F'
    };

    var ACTION_LABELS = {
        'Ban': 'Bans',
        'TempBan': 'Temp Bans',
        'Kick': 'Kicks',
        'Warning': 'Warnings',
        'Observation': 'Observations'
    };

    // --- Utilities ---

    function esc(str) {
        if (!str) return '';
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(str));
        return d.innerHTML;
    }

    function ago(dateStr) {
        if (!dateStr) return '';
        var diff = (Date.now() - new Date(dateStr).getTime()) / 1000;
        if (diff < 60) return 'just now';
        if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
        if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
        if (diff < 604800) return Math.floor(diff / 86400) + 'd ago';
        return new Date(dateStr).toLocaleDateString();
    }

    function getActionIcon(actionType) {
        return ACTION_ICONS[actionType] || '\u{1F4CB}';
    }

    function loadFilters() {
        try {
            var saved = localStorage.getItem('xi-pw-filters');
            if (saved) widgetState.filters = JSON.parse(saved);
        } catch (e) { /* ignore */ }
    }

    function saveFilters() {
        try {
            localStorage.setItem('xi-pw-filters', JSON.stringify(widgetState.filters));
        } catch (e) { /* ignore */ }
    }

    function isFiltered(actionType) {
        return widgetState.filters[actionType] === false;
    }

    // --- API ---

    function apiGet(path, cb) {
        var url = widgetState.portalUrl + path;
        if (widgetState.token) {
            url += (path.indexOf('?') >= 0 ? '&' : '?') + 'token=' + encodeURIComponent(widgetState.token);
        }
        var xhr = new XMLHttpRequest();
        xhr.open('GET', url, true);
        xhr.setRequestHeader('Accept', 'application/json');
        xhr.onload = function () {
            if (xhr.status >= 200 && xhr.status < 300) {
                try { cb(null, JSON.parse(xhr.responseText)); } catch (e) { cb(e, null); }
            } else {
                cb(new Error('HTTP ' + xhr.status), null);
            }
        };
        xhr.onerror = function () { cb(new Error('Network error'), null); };
        xhr.send();
    }

    function apiPost(path, body, cb) {
        var xhr = new XMLHttpRequest();
        xhr.open('POST', widgetState.portalUrl + path, true);
        xhr.setRequestHeader('Content-Type', 'application/json');
        xhr.setRequestHeader('Accept', 'application/json');
        xhr.onload = function () { cb(xhr.status >= 200 && xhr.status < 300); };
        xhr.onerror = function () { cb(false); };
        xhr.send(JSON.stringify(body));
    }

    // --- Rendering ---

    var lastData = null;

    function renderWidget(container, data) {
        lastData = data;
        widgetState.authenticated = data.authenticated;
        container.innerHTML = '';

        var wrapper = document.createElement('div');
        wrapper.className = 'xi-portal-widget';
        wrapper.innerHTML = buildHeader(data) + buildFilters(data) + buildBody(data) + buildFooter(data);
        container.appendChild(wrapper);

        bindEvents(container);
    }

    function buildHeader(data) {
        var bellLink = widgetState.portalUrl + (data.authenticated ? '/Profile/Notifications' : '/');
        var badge = '';
        if (data.authenticated && data.unreadCount > 0) {
            var countText = data.unreadCount > 99 ? '99+' : data.unreadCount;
            badge = '<span class="xi-pw-badge">' + countText + '</span>';
        }

        var userInfo = '';
        if (data.authenticated && data.displayName) {
            userInfo = '<span class="xi-pw-user">' + esc(data.displayName) + '</span>';
        }

        var title = '<a href="' + esc(bellLink) + '" class="xi-pw-bell-link" target="_blank" title="View all notifications">' +
            '<span class="xi-pw-bell">\u{1F514}</span>' + badge + '</a>' +
            '<span class="xi-pw-header-text">' + (data.authenticated ? 'Portal Activity' : 'Portal Activity') + '</span>';

        var actions = '';
        if (data.authenticated && data.unreadCount > 0) {
            actions = '<a href="#" class="xi-pw-mark-all" title="Mark all as read">\u2713 Read all</a>';
        }

        return '<div class="xi-pw-header">' +
            '<div class="xi-pw-title">' + title + '</div>' +
            '<div class="xi-pw-header-right">' + userInfo + actions + '</div>' +
            '</div>';
    }

    function buildFilters(data) {
        var items = data.notifications || [];
        // Collect unique action types from current data
        var types = {};
        for (var i = 0; i < items.length; i++) {
            var at = items[i].actionType;
            if (at && ACTION_LABELS[at]) types[at] = true;
        }
        var typeKeys = Object.keys(types);
        if (typeKeys.length < 2) return ''; // No point filtering with 0-1 types

        var html = '<div class="xi-pw-filters">';
        for (var j = 0; j < typeKeys.length; j++) {
            var t = typeKeys[j];
            var active = !isFiltered(t);
            html += '<button class="xi-pw-filter-btn' + (active ? ' xi-pw-filter-active' : '') + '" data-filter="' + esc(t) + '" title="' + esc(ACTION_LABELS[t]) + '">' +
                getActionIcon(t) + '</button>';
        }
        html += '</div>';
        return html;
    }

    function buildBody(data) {
        var items = data.notifications || [];
        // Apply filters
        var filtered = [];
        for (var i = 0; i < items.length; i++) {
            if (!items[i].actionType || !isFiltered(items[i].actionType)) {
                filtered.push(items[i]);
            }
        }

        if (filtered.length === 0) {
            return '<div class="xi-pw-empty">No notifications</div>';
        }

        var html = '<div class="xi-pw-list">';
        for (var j = 0; j < filtered.length; j++) {
            var n = filtered[j];
            var unread = (n.isRead === false) ? ' xi-pw-unread' : '';
            var url = n.actionUrl || (widgetState.portalUrl + '/');
            var icon = '';

            // Game icon
            if (n.iconUrl) {
                icon = '<img src="' + esc(n.iconUrl) + '" class="xi-pw-game-icon" alt="" onerror="this.style.display=\'none\'">';
            }

            // Action type icon
            var actionIcon = n.actionType ? ('<span class="xi-pw-action-icon" title="' + esc(n.actionType) + '">' + getActionIcon(n.actionType) + '</span>') : '';

            html += '<a href="' + esc(url) + '" class="xi-pw-item' + unread + '" target="_blank"' +
                (n.id ? ' data-nid="' + esc(String(n.id)) + '"' : '') + '>' +
                '<div class="xi-pw-item-row">' +
                '<div class="xi-pw-item-icons">' + icon + actionIcon + '</div>' +
                '<div class="xi-pw-item-content">' +
                '<div class="xi-pw-item-header">' +
                '<span class="xi-pw-item-title">' + esc(n.title) + '</span>' +
                '<span class="xi-pw-item-time">' + ago(n.createdAt) + '</span>' +
                '</div>' +
                '<div class="xi-pw-item-msg">' + esc(n.message) + '</div>' +
                '</div>' +
                '</div>' +
                '</a>';
        }
        html += '</div>';

        if (data.authenticated && data.unclaimed && data.unclaimed.hasItems) {
            html += '<a href="' + esc(data.unclaimed.url) + '" class="xi-pw-unclaimed" target="_blank">' +
                '\u26A0 There are unclaimed admin actions that need review' +
                '</a>';
        }

        return html;
    }

    function buildFooter(data) {
        var portalLink = data.portalUrl || widgetState.portalUrl;
        return '<div class="xi-pw-footer">' +
            '<a href="' + esc(portalLink) + (data.authenticated ? '/Profile/Notifications' : '') + '" target="_blank">' +
            (data.authenticated ? 'View all in Portal' : 'Open Portal') + '</a>' +
            '</div>';
    }

    // --- Events ---

    function bindEvents(container) {
        var markAllBtn = container.querySelector('.xi-pw-mark-all');
        if (markAllBtn) {
            markAllBtn.addEventListener('click', function (e) {
                e.preventDefault();
                apiPost('/api/external/notifications/read-all', { token: widgetState.token }, function (ok) {
                    if (ok) refresh();
                });
            });
        }

        var items = container.querySelectorAll('.xi-pw-item[data-nid]');
        for (var i = 0; i < items.length; i++) {
            items[i].addEventListener('click', function () {
                var nid = this.getAttribute('data-nid');
                if (nid && widgetState.token) {
                    apiPost('/api/external/notifications/' + nid + '/read', { token: widgetState.token }, function () { });
                }
            });
        }

        // Filter toggle buttons
        var filterBtns = container.querySelectorAll('.xi-pw-filter-btn');
        for (var j = 0; j < filterBtns.length; j++) {
            filterBtns[j].addEventListener('click', function (e) {
                e.preventDefault();
                var type = this.getAttribute('data-filter');
                if (widgetState.filters[type] === false) {
                    delete widgetState.filters[type];
                } else {
                    widgetState.filters[type] = false;
                }
                saveFilters();
                if (lastData) {
                    var cont = document.getElementById('portal-notifications-widget');
                    if (cont) renderWidget(cont, lastData);
                }
            });
        }
    }

    // --- Lifecycle ---

    function refresh() {
        var container = document.getElementById('portal-notifications-widget');
        if (!container) return;

        apiGet('/api/external/notifications?take=20', function (err, data) {
            if (err) {
                container.innerHTML = '<div class="xi-pw-error">Unable to load notifications</div>';
                return;
            }
            try {
                renderWidget(container, data);
            } catch (e) {
                console.error('Forum widget render error:', e);
                container.innerHTML = '<div class="xi-pw-error">Unable to display notifications</div>';
            }
        });
    }

    function init() {
        var container = document.getElementById('portal-notifications-widget');
        if (!container) return;

        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }

        widgetState.token = container.getAttribute('data-token') || '';
        widgetState.portalUrl = (container.getAttribute('data-portal-url') || '').replace(/\/+$/, '');

        if (!widgetState.portalUrl) {
            container.innerHTML = '<div class="xi-pw-error">Portal URL not configured</div>';
            return;
        }

        loadFilters();
        container.innerHTML = '<div class="xi-pw-loading">Loading notifications...</div>';
        refresh();

        if (widgetState.token) {
            pollTimer = setInterval(refresh, POLL_INTERVAL);
        }
    }

    // --- Styles ---

    function injectStyles() {
        if (document.getElementById('xi-pw-styles')) return;
        var style = document.createElement('style');
        style.id = 'xi-pw-styles';
        style.textContent =
            '.xi-portal-widget{font-family:inherit;font-size:13px;border:1px solid #333;border-radius:4px;background:#262626;overflow:hidden;width:100%;color:#ccc}' +
            '.xi-pw-header{display:flex;justify-content:space-between;align-items:center;padding:8px 12px;background:#1a1a1a;color:#e0e0e0;font-weight:600;border-bottom:1px solid #333}' +
            '.xi-pw-title{display:flex;align-items:center;gap:6px}' +
            '.xi-pw-header-right{display:flex;align-items:center;gap:8px}' +
            '.xi-pw-header-text{font-size:13px}' +
            '.xi-pw-bell-link{text-decoration:none;display:flex;align-items:center;position:relative}' +
            '.xi-pw-bell{font-size:18px;cursor:pointer}' +
            '.xi-pw-badge{background:#c0392b;color:#fff;font-size:10px;padding:1px 5px;border-radius:10px;position:absolute;top:-4px;right:-8px;min-width:14px;text-align:center}' +
            '.xi-pw-user{font-size:11px;color:#7eaac4;font-weight:400}' +
            '.xi-pw-mark-all{color:#7eaac4;font-size:11px;text-decoration:none;white-space:nowrap}' +
            '.xi-pw-mark-all:hover{color:#aed6f1}' +
            '.xi-pw-filters{display:flex;gap:4px;padding:6px 12px;background:#1f1f1f;border-bottom:1px solid #333}' +
            '.xi-pw-filter-btn{background:none;border:1px solid #444;border-radius:3px;padding:2px 8px;cursor:pointer;font-size:13px;color:#777;transition:all .15s}' +
            '.xi-pw-filter-btn:hover{border-color:#666;color:#ccc}' +
            '.xi-pw-filter-active{border-color:#3498db;color:#e0e0e0;background:#1e2a35}' +
            '.xi-pw-list{max-height:400px;overflow-y:auto}' +
            '.xi-pw-item{display:block;padding:8px 12px;border-bottom:1px solid #333;text-decoration:none;color:#ccc;transition:background .15s}' +
            '.xi-pw-item:hover{background:#2f2f2f;text-decoration:none;color:#fff}' +
            '.xi-pw-unread{background:#1e2a35;border-left:3px solid #3498db}' +
            '.xi-pw-item-row{display:flex;gap:8px;align-items:flex-start}' +
            '.xi-pw-item-icons{display:flex;flex-direction:column;align-items:center;gap:2px;flex-shrink:0;min-width:24px;padding-top:1px}' +
            '.xi-pw-game-icon{width:20px;height:20px;border-radius:2px}' +
            '.xi-pw-action-icon{font-size:12px;line-height:1}' +
            '.xi-pw-item-content{flex:1;min-width:0}' +
            '.xi-pw-item-header{display:flex;justify-content:space-between;align-items:baseline;gap:8px}' +
            '.xi-pw-item-title{font-weight:600;font-size:12px;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#e0e0e0}' +
            '.xi-pw-item-time{font-size:11px;color:#777;white-space:nowrap}' +
            '.xi-pw-item-msg{font-size:12px;color:#999;margin-top:2px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}' +
            '.xi-pw-unclaimed{display:block;padding:8px 12px;background:#3d2f0a;color:#f0c040;font-size:12px;text-decoration:none;border-top:1px solid #4a3a10}' +
            '.xi-pw-unclaimed:hover{background:#4a3a10;text-decoration:none;color:#ffd54f}' +
            '.xi-pw-footer{padding:8px 12px;text-align:center;background:#1a1a1a;border-top:1px solid #333}' +
            '.xi-pw-footer a{color:#7eaac4;text-decoration:none;font-size:12px;font-weight:500}' +
            '.xi-pw-footer a:hover{color:#aed6f1;text-decoration:underline}' +
            '.xi-pw-empty{padding:24px;text-align:center;color:#777}' +
            '.xi-pw-loading{padding:24px;text-align:center;color:#777}' +
            '.xi-pw-error{padding:16px;text-align:center;color:#e74c3c;font-size:12px}';
        document.head.appendChild(style);
    }

    // --- Bootstrap ---

    injectStyles();

    window.addEventListener('pagehide', function () {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
