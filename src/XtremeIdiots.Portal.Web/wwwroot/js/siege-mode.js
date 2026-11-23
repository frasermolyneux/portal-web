(function () {
    'use strict';
    var status = document.getElementById('siege-status');
    if (!status || !window.PortalAsync) return;

    var poller = window.PortalAsync.createPoller({
        intervalMs: 10000,
        load: function (signal) {
            if (status.contains(document.activeElement)) return Promise.resolve();
            return fetch(status.dataset.url, { credentials: 'same-origin', signal: signal })
                .then(function (response) {
                    if (!response.ok || response.redirected) throw new Error('Siege Mode status unavailable');
                    return response.text();
                }).then(function (html) { window.PortalAsync.renderHtml(status, html); });
        },
        onError: function () {
            status.textContent = 'Siege Mode status is unavailable. Retrying...';
        }
    });
    poller.start();
}());
