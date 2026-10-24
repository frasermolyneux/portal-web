(function () {
    'use strict';

    function renderHtml(element, html) {
        element.replaceChildren(DOMPurify.sanitize(html, { RETURN_DOM_FRAGMENT: true, ADD_ATTR: ['target'] }));
        enhanceDates(element);
    }

    function enhanceDates(element) {
        if (window.portalDate) window.portalDate.enhanceDateElements(element);
    }

    function createPoller(options) {
        var timer = null;
        var active = null;
        var controller = null;
        var running = false;
        var disposed = false;
        var suspended = false;
        var resumeOnShow = false;
        var failures = 0;
        var interval = options.intervalMs || 10000;

        function schedule() {
            clearTimeout(timer);
            if (!running || disposed || suspended || document.hidden) return;
            var delay = Math.min(interval * Math.pow(2, failures), options.maxBackoffMs || interval * 8);
            timer = setTimeout(refresh, delay);
        }

        function refresh() {
            if (disposed || suspended || document.hidden) return Promise.resolve();
            if (active) return active;
            clearTimeout(timer);
            controller = new AbortController();
            var timedOut = false;
            var deadline = setTimeout(function () {
                timedOut = true;
                controller.abort();
            }, options.timeoutMs || 30000);
            active = Promise.resolve().then(function () {
                return options.load(controller.signal);
            }).then(function () {
                failures = 0;
            }).catch(function (error) {
                if (error.name === 'AbortError' && !timedOut) return;
                failures = Math.min(failures + 1, 8);
                if (options.onError) options.onError(error);
                else console.warn('Background refresh unavailable:', error);
            }).finally(function () {
                clearTimeout(deadline);
                active = null;
                controller = null;
                schedule();
            });
            return active;
        }

        function visibilityChanged() {
            clearTimeout(timer);
            if (running && !document.hidden) refresh();
        }

        function stop() {
            running = false;
            resumeOnShow = false;
            clearTimeout(timer);
            if (controller) controller.abort();
        }

        function dispose() {
            disposed = true;
            stop();
            document.removeEventListener('visibilitychange', visibilityChanged);
            window.removeEventListener('pagehide', pageHidden);
            window.removeEventListener('pageshow', pageShown);
        }

        function pageHidden(event) {
            if (!event.persisted) {
                dispose();
                return;
            }
            suspended = true;
            resumeOnShow = running;
            running = false;
            clearTimeout(timer);
            if (controller) controller.abort();
        }

        function pageShown(event) {
            if (!event.persisted || !suspended || disposed) return;
            suspended = false;
            var resume = resumeOnShow;
            resumeOnShow = false;
            if (!resume) return;
            running = true;
            if (active) void active.then(function () { if (running && !disposed) refresh(); });
            else refresh();
        }

        document.addEventListener('visibilitychange', visibilityChanged);
        window.addEventListener('pagehide', pageHidden);
        window.addEventListener('pageshow', pageShown);
        return {
            start: function () {
                if (disposed) return;
                running = true;
                refresh();
            },
            stop: stop,
            dispose: dispose,
            refresh: refresh
        };
    }

    function loadSection(element, url, options) {
        options = options || {};
        if (typeof element === 'string') element = document.querySelector(element);
        if (!element) return Promise.resolve();
        if (element.portalSectionRequest) return element.portalSectionRequest;
        var controller = new AbortController();
        var timedOut = false;
        var deadline = setTimeout(function () {
            timedOut = true;
            controller.abort();
        }, options.timeoutMs || 30000);
        function abort(event) {
            controller.abort();
            if (event.persisted) {
                window.addEventListener('pageshow', function () {
                    void Promise.resolve(element.portalSectionRequest).then(function () {
                        if (element.isConnected) loadSection(element, url, options);
                    });
                }, { once: true });
            }
        }
        window.addEventListener('pagehide', abort, { once: true });
        element.setAttribute('aria-busy', 'true');
        element.portalSectionRequest = fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'text/html' },
            signal: controller.signal
        }).then(function (response) {
            if (!response.ok || response.redirected) throw new Error('Section request failed (' + response.status + ')');
            return options.render ? response.json() : response.text();
        }).then(function (payload) {
            if (controller.signal.aborted) return;
            if (options.render) {
                options.render(element, payload);
                enhanceDates(element);
            }
            else renderHtml(element, payload);
            if (options.onLoaded) options.onLoaded(element);
            element.dispatchEvent(new CustomEvent('portal:section-loaded', { bubbles: true }));
        }).catch(function (error) {
            if (error.name === 'AbortError' && !timedOut) return;
            console.warn('Optional section unavailable:', error);
            element.replaceChildren();
            var message = document.createElement('span');
            message.className = 'text-muted';
            message.textContent = 'Data unavailable. ';
            var retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-secondary';
            retry.textContent = 'Retry';
            retry.addEventListener('click', function () { loadSection(element, url, options); });
            element.append(message, retry);
            if (options.onError) options.onError(error, element);
        }).finally(function () {
            clearTimeout(deadline);
            element.removeAttribute('aria-busy');
            element.portalSectionRequest = null;
            window.removeEventListener('pagehide', abort);
        });
        return element.portalSectionRequest;
    }

    window.PortalAsync = { createPoller: createPoller, loadSection: loadSection, renderHtml: renderHtml };
})();
