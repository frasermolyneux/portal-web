// notifications.js - Notification bell functionality
(function () {
    'use strict';

    var POLL_INTERVAL = 60000; // 60 seconds
    var pollTimer = null;

    function getAntiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    function fetchUnreadCount() {
        $.ajax({
            url: '/api/notifications/unread-count',
            type: 'GET',
            dataType: 'json',
            success: function (data) {
                updateBadge(data.count);
            },
            error: function () { /* silently fail */ }
        });
    }

    function updateBadge(count) {
        var badge = document.getElementById('notificationBadge');
        if (!badge) return;
        if (count > 0) {
            badge.textContent = count > 99 ? '99+' : count;
            badge.classList.remove('d-none');
        } else {
            badge.classList.add('d-none');
        }
    }

    function fetchRecentNotifications() {
        var list = document.getElementById('notificationList');
        if (!list) return;

        list.innerHTML = '<div class="text-center py-3 text-muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading...</div>';

        $.ajax({
            url: '/api/notifications/recent?take=10',
            type: 'GET',
            dataType: 'json',
            success: function (data) {
                renderNotifications(data);
            },
            error: function () {
                list.innerHTML = '<div class="text-center py-3 text-muted"><i class="fa-solid fa-exclamation-triangle"></i> Failed to load</div>';
            }
        });
    }

    function renderNotifications(notifications) {
        var list = document.getElementById('notificationList');
        if (!list) return;

        if (!notifications || notifications.length === 0) {
            list.innerHTML = '<div class="text-center py-3 text-muted"><i class="fa-solid fa-check-circle"></i> No notifications</div>';
            return;
        }

        var html = notifications.map(function (n) {
            var readClass = n.isRead ? '' : ' bg-light';
            var actionUrl = n.actionUrl || '#';
            return '<a href="' + escapeHtml(actionUrl) + '" class="dropdown-item notification-item py-2 px-3' + readClass + '" ' +
                'data-notification-id="' + escapeHtml(String(n.notificationId)) + '" style="white-space: normal;">' +
                '<div class="d-flex justify-content-between">' +
                '<strong class="small">' + escapeHtml(n.title || '') + '</strong>' +
                '<small class="text-muted ms-2 flex-shrink-0">' + portalDate.formatRelativeTime(n.createdAt) + '</small>' +
                '</div>' +
                '<div class="small text-muted text-truncate">' + escapeHtml(n.message || '') + '</div>' +
                '</a>';
        }).join('<div class="dropdown-divider my-0"></div>');

        list.innerHTML = html;
    }

    function markAsRead(notificationId) {
        var token = getAntiForgeryToken();
        var headers = {};
        if (token) {
            headers['RequestVerificationToken'] = token;
        }

        $.ajax({
            url: '/api/notifications/' + notificationId + '/read',
            type: 'POST',
            headers: headers
        });
    }

    function markAllAsRead() {
        var token = getAntiForgeryToken();
        var headers = {};
        if (token) {
            headers['RequestVerificationToken'] = token;
        }

        $.ajax({
            url: '/api/notifications/read-all',
            type: 'POST',
            headers: headers,
            success: function () {
                fetchUnreadCount();
                fetchRecentNotifications();
            }
        });
    }

    function init() {
        var bell = document.getElementById('notificationBell');
        if (!bell) return;

        // Fetch unread count on load
        fetchUnreadCount();

        // Load notifications when dropdown opens
        var dropdown = document.getElementById('notificationDropdown');
        if (dropdown) {
            dropdown.addEventListener('show.bs.dropdown', function () {
                fetchRecentNotifications();
            });
        }

        // Mark all as read
        var markAllBtn = document.getElementById('markAllReadBtn');
        if (markAllBtn) {
            markAllBtn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                markAllAsRead();
            });
        }

        // Mark individual as read on click
        $(document).on('click', '.notification-item', function () {
            var notificationId = $(this).data('notification-id');
            if (notificationId) {
                markAsRead(notificationId);
            }
        });

        // Poll for updates
        pollTimer = setInterval(fetchUnreadCount, POLL_INTERVAL);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
