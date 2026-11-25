(function () {
    'use strict';
    var status = document.getElementById('siege-status');
    if (!status || !window.PortalAsync) return;
    var enable = document.getElementById('siege-enable');
    var disable = document.getElementById('siege-disable');

    var errorMessage = document.createElement('p');
    errorMessage.className = 'text-warning';
    errorMessage.setAttribute('role', 'status');
    errorMessage.hidden = true;
    status.before(errorMessage);

    var poller = window.PortalAsync.createPoller({
        intervalMs: 10000,
        load: function (signal) {
            if (status.contains(document.activeElement)) return Promise.resolve();
            return fetch(status.dataset.url, { credentials: 'same-origin', signal: signal })
                .then(function (response) {
                    if (!response.ok || response.redirected) throw new Error('Siege Mode status unavailable');
                    return response.text();
                }).then(function (html) {
                    errorMessage.hidden = true;
                    if (status.contains(document.activeElement)) return;

                    var input = status.querySelector('#siege-extension-minutes');
                    var session = input && input.form && input.form.querySelector('[name="sessionId"]');
                    var minutes = input && input.value;
                    var sessionId = session && session.value;

                    window.PortalAsync.renderHtml(status, html);

                    var nextInput = status.querySelector('#siege-extension-minutes');
                    var nextSession = nextInput && nextInput.form && nextInput.form.querySelector('[name="sessionId"]');
                    if (enable && disable) {
                        enable.hidden = !!nextSession;
                        disable.hidden = !nextSession;
                        disable.querySelector('[name="sessionId"]').value = nextSession ? nextSession.value : '';
                    }
                    if (sessionId && nextSession && nextSession.value === sessionId) {
                        nextInput.value = minutes;
                    }
                });
        },
        onError: function () {
            if (enable) enable.hidden = true;
            if (disable) disable.hidden = true;
            errorMessage.textContent = 'Siege Mode status is unavailable. Retrying...';
            errorMessage.hidden = false;
        }
    });
    poller.start();
}());
