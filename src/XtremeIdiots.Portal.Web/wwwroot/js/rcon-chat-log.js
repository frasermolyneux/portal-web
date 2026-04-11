/**
 * Live Chat Log module — polls server-specific chat messages and supports say command.
 * Lifecycle: init(options) → start() → stop() → dispose()
 */
var RconChatLog = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _intervalId = null;
    var _refreshEnabled = true;
    var _lastRefreshTime = new Date();
    var _lastMessageId = null;
    var _selectors = {};

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _lastMessageId = null;
        _selectors = {
            container: options.containerSelector || '#chatLogContainer',
            messages: options.messagesSelector || '#chatMessages',
            messageCount: options.messageCountSelector || '#chatMessageCount',
            refreshBadge: options.refreshBadgeId || 'chatLogRefresh',
            sayForm: options.sayFormSelector || '#sayForm',
            sayInput: options.sayInputSelector || '#sayMessage'
        };
    }

    function load() {
        if (!_refreshEnabled || !_serverId) return;

        var url = '/ServerAdmin/GetServerLiveChatLog/' + _serverId + '?minutes=30';
        if (_lastMessageId) {
            url += '&lastMessageId=' + _lastMessageId;
        }

        $.ajax({
            url: url,
            type: 'GET',
            success: function (result) {
                if (result.messages && result.messages.length > 0) {
                    _lastMessageId = result.messages[0].chatMessageId;

                    var messagesHtml = '';
                    for (var i = 0; i < result.messages.length; i++) {
                        var msg = result.messages[i];
                        var msgClass = msg.locked ? 'alert-warning' : 'alert-info';
                        var lockedBadge = msg.locked ? '<span class="badge bg-warning ms-2">Locked</span>' : '';

                        messagesHtml += '<div class="alert ' + msgClass + ' alert-sm mb-1">';
                        messagesHtml += '<small class="text-muted">' + portalDate.formatRelativeTime(msg.timestamp) + '</small> ';

                        if (msg.playerId) {
                            messagesHtml += '<strong><a href="/Players/Details/' + msg.playerId + '" class="alert-link">' + escapeHtml(msg.username) + '</a></strong>';
                        } else {
                            messagesHtml += '<strong>' + escapeHtml(msg.username) + '</strong>';
                        }

                        messagesHtml += lockedBadge + ': ';
                        messagesHtml += escapeHtml(msg.message);
                        messagesHtml += '</div>';
                    }

                    var container = $(_selectors.messages);
                    if (container.find('p.text-muted').length > 0) {
                        container.html(messagesHtml);
                        $(_selectors.messageCount).text(result.messages.length);
                    } else {
                        container.prepend(messagesHtml);
                        $(_selectors.messageCount).text(result.messages.length + ' new');
                    }

                    $(_selectors.container).scrollTop(0);
                    _lastRefreshTime = new Date();
                    RconUtils.updateRefreshBadge(_selectors.refreshBadge, _lastRefreshTime);
                } else if (!_lastMessageId) {
                    $(_selectors.messages).html('<p class="text-muted text-center">No recent chat messages</p>');
                    $(_selectors.messageCount).text('0');
                } else {
                    $(_selectors.messageCount).text('0 new');
                }
            },
            error: function (xhr) {
                console.error('Failed to load chat log:', xhr);
                if (!_lastMessageId) {
                    $(_selectors.messages).html('<p class="text-danger text-center">Failed to load chat messages</p>');
                }
            }
        });
    }

    function bindSayForm() {
        $(_selectors.sayForm).off('submit').on('submit', function (e) {
            e.preventDefault();
            var message = $(_selectors.sayInput).val().trim();
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
                        setTimeout(load, 1000);
                    } else {
                        RconUtils.showToast('error', result.message || 'Failed to send message');
                    }
                },
                error: function (xhr) {
                    RconUtils.showToast('error', 'Error sending message: ' + (xhr.responseJSON?.message || xhr.statusText || 'Unknown error'));
                }
            });
        });
    }

    function setRefreshEnabled(enabled) {
        _refreshEnabled = enabled;
    }

    function start(intervalMs) {
        load();
        bindSayForm();
        if (_intervalId) clearInterval(_intervalId);
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
        if (_selectors.sayForm) $(_selectors.sayForm).off('submit');
        _lastMessageId = null;
        _serverId = null;
    }

    return {
        init: init,
        load: load,
        setRefreshEnabled: setRefreshEnabled,
        start: start,
        stop: stop,
        dispose: dispose
    };
})();
