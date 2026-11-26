(function () {
    'use strict';

    document.querySelectorAll('[data-rules-audit-flow]').forEach(function (details) {
        const content = details.querySelector('[data-flow-content]');
        const status = details.querySelector('[data-flow-status]');
        function loadFlow() {
            if (!details.open || content.dataset.loaded || content.portalSectionRequest) return;

            status.textContent = content.textContent = 'Loading execution flow...';
            void PortalAsync.loadSection(content, details.dataset.rulesAuditFlow, {
                onLoaded: function () {
                    content.dataset.loaded = 'true';
                    status.textContent = 'Execution flow loaded.';
                },
                onError: function () { status.textContent = 'Execution flow unavailable.'; }
            });
        }

        details.addEventListener('toggle', loadFlow);
        loadFlow();
    });
})();
