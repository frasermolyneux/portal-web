(function () {
    'use strict';
    var banner = document.getElementById('siege-banner');
    if (!banner || !window.PortalAsync) return;
    window.PortalAsync.createPoller({
        intervalMs: 30000,
        load: function (signal) {
            return fetch(banner.dataset.url, { credentials: 'same-origin', signal: signal })
                .then(function (response) {
                    if (!response.ok || response.redirected) throw new Error('Siege Mode banner unavailable');
                    return response.text();
                }).then(function (html) { window.PortalAsync.renderHtml(banner, html); });
        },
        onError: function () { banner.textContent = 'Siege Mode status is unavailable.'; }
    }).start();
}());
