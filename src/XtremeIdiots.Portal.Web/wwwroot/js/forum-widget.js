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
    var widgetState = { token: null, portalUrl: '', authenticated: false };

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

    function renderWidget(container, data) {
        widgetState.authenticated = data.authenticated;
        container.innerHTML = '';

        var wrapper = document.createElement('div');
        wrapper.className = 'xi-portal-widget';
        wrapper.innerHTML = buildHeader(data) + buildBody(data) + buildFooter(data);
        container.appendChild(wrapper);

        bindEvents(container);
    }

    function buildHeader(data) {
        var badge = '';
        if (data.authenticated && data.unreadCount > 0) {
            var countText = data.unreadCount > 99 ? '99+' : data.unreadCount;
            badge = '<span class="xi-pw-badge">' + countText + '</span>';
        }

        var title = data.authenticated
            ? '<span class="xi-pw-bell">&#128276;</span> Notifications ' + badge
            : '<span class="xi-pw-bell">&#128276;</span> Portal Activity';

        var actions = '';
        if (data.authenticated && data.unreadCount > 0) {
            actions = '<a href="#" class="xi-pw-mark-all" title="Mark all as read">&#10003; Read all</a>';
        }

        return '<div class="xi-pw-header">' +
            '<div class="xi-pw-title">' + title + '</div>' +
            actions +
            '</div>';
    }

    function buildBody(data) {
        var items = data.notifications || [];
        if (items.length === 0) {
            return '<div class="xi-pw-empty">No notifications</div>';
        }

        var html = '<div class="xi-pw-list">';
        for (var i = 0; i < items.length; i++) {
            var n = items[i];
            var unread = (n.isRead === false) ? ' xi-pw-unread' : '';
            var url = n.actionUrl || (widgetState.portalUrl + '/');
            html += '<a href="' + esc(url) + '" class="xi-pw-item' + unread + '" target="_blank"' +
                (n.id ? ' data-nid="' + esc(String(n.id)) + '"' : '') + '>' +
                '<div class="xi-pw-item-header">' +
                '<span class="xi-pw-item-title">' + esc(n.title) + '</span>' +
                '<span class="xi-pw-item-time">' + ago(n.createdAt) + '</span>' +
                '</div>' +
                '<div class="xi-pw-item-msg">' + esc(n.message) + '</div>' +
                '</a>';
        }
        html += '</div>';

        // Unclaimed actions banner
        if (data.authenticated && data.unclaimed && data.unclaimed.hasItems) {
            html += '<a href="' + esc(data.unclaimed.url) + '" class="xi-pw-unclaimed" target="_blank">' +
                '&#9888; There are unclaimed admin actions that need review' +
                '</a>';
        }

        return html;
    }

    function buildFooter(data) {
        var portalLink = data.portalUrl || widgetState.portalUrl;
        if (data.authenticated) {
            return '<div class="xi-pw-footer">' +
                '<a href="' + esc(portalLink) + '/Profile/Notifications" target="_blank">View all in Portal</a>' +
                '</div>';
        }
        return '<div class="xi-pw-footer">' +
            '<a href="' + esc(portalLink) + '" target="_blank">Open Portal</a>' +
            '</div>';
    }

    // --- Events ---

    function bindEvents(container) {
        // Mark all as read
        var markAllBtn = container.querySelector('.xi-pw-mark-all');
        if (markAllBtn) {
            markAllBtn.addEventListener('click', function (e) {
                e.preventDefault();
                apiPost('/api/external/notifications/read-all', { token: widgetState.token }, function (ok) {
                    if (ok) refresh();
                });
            });
        }

        // Mark individual as read on click
        var items = container.querySelectorAll('.xi-pw-item[data-nid]');
        for (var i = 0; i < items.length; i++) {
            items[i].addEventListener('click', function () {
                var nid = this.getAttribute('data-nid');
                if (nid && widgetState.token) {
                    apiPost('/api/external/notifications/' + nid + '/read', { token: widgetState.token }, function () { });
                }
            });
        }
    }

    // --- Lifecycle ---

    function refresh() {
        var container = document.getElementById('portal-notifications-widget');
        if (!container) return;

        apiGet('/api/external/notifications?take=15', function (err, data) {
            if (err) {
                container.innerHTML = '<div class="xi-pw-error">Unable to load notifications</div>';
                return;
            }
            renderWidget(container, data);
        });
    }

    function init() {
        var container = document.getElementById('portal-notifications-widget');
        if (!container) return;

        // Clear any existing interval from prior init calls
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

        // Initial load
        container.innerHTML = '<div class="xi-pw-loading">Loading notifications...</div>';
        refresh();

        // Poll for updates if authenticated
        if (widgetState.token) {
            pollTimer = setInterval(refresh, POLL_INTERVAL);
        }
    }

    // --- Styles (injected once) ---

    function injectStyles() {
        if (document.getElementById('xi-pw-styles')) return;
        var style = document.createElement('style');
        style.id = 'xi-pw-styles';
        style.textContent =
            '.xi-portal-widget{font-family:inherit;font-size:13px;border:1px solid #333;border-radius:4px;background:#262626;overflow:hidden;max-width:400px;color:#ccc}' +
            '.xi-pw-header{display:flex;justify-content:space-between;align-items:center;padding:8px 12px;background:#1a1a1a;color:#e0e0e0;font-weight:600;border-bottom:1px solid #333}' +
            '.xi-pw-title{display:flex;align-items:center;gap:6px}' +
            '.xi-pw-bell{font-size:16px}' +
            '.xi-pw-badge{background:#c0392b;color:#fff;font-size:11px;padding:1px 6px;border-radius:10px;margin-left:4px}' +
            '.xi-pw-mark-all{color:#7eaac4;font-size:11px;text-decoration:none;white-space:nowrap}' +
            '.xi-pw-mark-all:hover{color:#aed6f1}' +
            '.xi-pw-list{max-height:360px;overflow-y:auto}' +
            '.xi-pw-item{display:block;padding:8px 12px;border-bottom:1px solid #333;text-decoration:none;color:#ccc;transition:background .15s}' +
            '.xi-pw-item:hover{background:#2f2f2f;text-decoration:none;color:#fff}' +
            '.xi-pw-unread{background:#1e2a35;border-left:3px solid #3498db}' +
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
