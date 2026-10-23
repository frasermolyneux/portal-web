(function () {
    'use strict';

    function initializeMap() {
        const element = document.getElementById('map');
        if (!element || element.dataset.portalMapInitialized || !window.google?.maps || !element.dataset.latitude || !element.dataset.longitude) return;
        const position = { lat: Number(element.dataset.latitude), lng: Number(element.dataset.longitude) };
        if (!Number.isFinite(position.lat) || !Number.isFinite(position.lng)) return;
        element.dataset.portalMapInitialized = 'true';
        const map = new google.maps.Map(element, { zoom: 4, center: position });
        new google.maps.Marker({ position: position, map: map, title: 'IP Location' });
    }
    document.addEventListener('portal:maps-ready', initializeMap);

    function retryButton(element, retry) {
        element.querySelectorAll('[data-intelligence-retry]').forEach(message => message.remove());
        const message = document.createElement('p');
        message.dataset.intelligenceRetry = 'true';
        message.setAttribute('role', 'status');
        message.className = 'text-muted';
        message.textContent = 'IP intelligence unavailable. ';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary btn-sm';
        button.textContent = 'Retry';
        button.addEventListener('click', function () { message.remove(); retry(); });
        message.append(button);
        element.append(message);
    }

    document.addEventListener('DOMContentLoaded', function () {
        const current = document.querySelector('[data-player-intelligence-url]');
        if (current) {
            window.PortalAsync.loadSection(current, current.dataset.playerIntelligenceUrl, { onLoaded: initializeMap });
            current.addEventListener('click', function (event) {
                if (event.target.closest('[data-retry-player-intelligence]'))
                    window.PortalAsync.loadSection(current, current.dataset.playerIntelligenceUrl, { onLoaded: initializeMap });
            });
        }

        const history = document.querySelector('[data-ip-history-url]');
        let historyRequest = null;
        let historyExpires = 0;
        async function loadHistory() {
            if (!history || historyRequest || historyExpires > Date.now()) return;
            history.querySelectorAll('[data-intelligence-retry]').forEach(message => message.remove());
            const controller = new AbortController();
            historyRequest = controller;
            try {
                const response = await fetch(history.dataset.ipHistoryUrl, { signal: controller.signal });
                if (!response.ok || response.redirected) throw new Error('IP history unavailable');
                const json = await response.json();
                if (!history.isConnected) return;
                let expires = Date.now() + 30000;
                json.data.forEach(function (intel) {
                    expires = Math.min(expires, Date.parse(intel.expiresAtUtc));
                    history.querySelectorAll('[data-history-ip]').forEach(function (cell) {
                        if (cell.dataset.historyIp !== intel.address) return;
                        const html = formatIPAddress(intel.address, intel.proxyCheckRiskScore, intel.isProxy,
                            intel.isVpn, intel.proxyType || '', intel.countryCode || '') +
                            (intel.status === 'unavailable' ? ' <small class="text-muted">Risk unavailable</small>' : '');
                        window.PortalAsync.renderHtml(cell, html);
                        if ($.fn.DataTable.isDataTable('#ipAddressesTable'))
                            $('#ipAddressesTable').DataTable().cell(cell).invalidate('dom');
                    });
                });
                historyExpires = expires;
                if (json.data.some(intel => intel.status === 'unavailable'))
                    retryButton(history, function () { historyExpires = 0; loadHistory(); });
            } catch (error) {
                if (error.name !== 'AbortError') retryButton(history, loadHistory);
            } finally {
                if (historyRequest === controller) historyRequest = null;
            }
        }
        document.getElementById('ipAddresses-tab')?.addEventListener('shown.bs.tab', loadHistory);
        if (history?.classList.contains('active')) loadHistory();

        const ipSection = document.querySelector('[data-ip-intelligence-url]');
        let ipRequest = null;
        async function loadIpDetails() {
            if (!ipSection || ipRequest) return;
            ipSection.querySelectorAll('[data-intelligence-retry]').forEach(message => message.remove());
            const controller = new AbortController();
            ipRequest = controller;
            try {
                const response = await fetch(ipSection.dataset.ipIntelligenceUrl, { signal: controller.signal });
                if (!response.ok || response.redirected) throw new Error('IP intelligence unavailable');
                const html = new DOMParser().parseFromString(await response.text(), 'text/html');
                const enriched = html.getElementById('ip-intelligence-content');
                if (!ipSection.isConnected) return;
                if (!enriched) throw new Error('IP intelligence unavailable');
                // Only replace optional intelligence; keep the independently loaded associated-player data.
                const columns = ipSection.children;
                const newColumns = enriched.children;
                columns[1].replaceWith(newColumns[1].cloneNode(true));
                columns[2].replaceWith(newColumns[2].cloneNode(true));
                columns[0].querySelector('h2').replaceWith(newColumns[0].querySelector('h2').cloneNode(true));
                const stats = columns[0].querySelector('.stats-container');
                Array.from(stats.children).slice(1).forEach(element => element.remove());
                Array.from(newColumns[0].querySelector('.stats-container').children).slice(1)
                    .forEach(element => stats.append(element.cloneNode(true)));
                initializeMap();
                if (enriched.dataset.intelligenceStatus === 'unavailable') retryButton(ipSection, loadIpDetails);
            } catch (error) {
                if (error.name !== 'AbortError') retryButton(ipSection, loadIpDetails);
            } finally {
                if (ipRequest === controller) ipRequest = null;
            }
        }
        loadIpDetails();
        window.addEventListener('pagehide', function () { historyRequest?.abort(); ipRequest?.abort(); });
        window.addEventListener('load', initializeMap);
    });
})();
