(function () {
    'use strict';
    var status = document.getElementById('siege-status');
    if (!status || !window.PortalAsync) return;

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
                    if (sessionId && nextSession && nextSession.value === sessionId) {
                        nextInput.value = minutes;
                    }
                });
        },
        onError: function () {
            errorMessage.textContent = 'Siege Mode status is unavailable. Retrying...';
            errorMessage.hidden = false;
        }
    });
    poller.start();
}());
