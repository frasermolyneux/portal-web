/**
 * Portal Date Utilities
 *
 * Locale-aware date formatting using native Intl APIs.
 * Uses navigator.language for locale and the browser's timezone automatically.
 * All date inputs are expected as ISO 8601 UTC strings.
 */
var portalDate = (function () {
    'use strict';

    function getLocale() {
        return navigator.language || 'en';
    }

    function getTimezone() {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone;
        } catch (e) {
            return undefined;
        }
    }

    /**
     * Parse an ISO 8601 UTC string into a Date object.
     * Returns null if the input is falsy or unparseable.
     */
    function parseUtc(isoString) {
        if (!isoString) return null;
        var d = new Date(isoString);
        return isNaN(d.getTime()) ? null : d;
    }

    /**
     * Format a date as a full localized datetime string.
     * @param {string} isoString - ISO 8601 UTC date string
     * @param {object} [options] - { showRelative: bool } appends relative time in muted text
     * @returns {string} Formatted datetime (may contain HTML if showRelative is true)
     */
    function formatDateTime(isoString, options) {
        if (!isoString) return '';
        var d = parseUtc(isoString);
        if (!d) return String(isoString);

        var opts = options || {};
        var formatted = d.toLocaleString(getLocale(), {
            year: 'numeric', month: 'long', day: 'numeric',
            hour: '2-digit', minute: '2-digit'
        });

        if (opts.showRelative) {
            var relative = computeRelativeTime(d);
            if (relative) {
                formatted += ' <span class="text-muted">(' + escapeAttr(relative) + ')</span>';
            }
        }

        return formatted;
    }

    /**
     * Format a date as a relative time string (e.g., "5 minutes ago").
     */
    function formatRelativeTime(isoString) {
        if (!isoString) return '';
        var d = parseUtc(isoString);
        if (!d) return String(isoString);
        return computeRelativeTime(d) || String(isoString);
    }

    /**
     * Format a date as a localized date-only string (no time component).
     */
    function formatDate(isoString) {
        if (!isoString) return '';
        var d = parseUtc(isoString);
        if (!d) return String(isoString);
        return d.toLocaleDateString(getLocale(), {
            year: 'numeric', month: 'long', day: 'numeric'
        });
    }

    /**
     * Format an expiry date with active/expired badge.
     * Status is server-authoritative — isExpired and isPermanent come from the API.
     */
    function formatExpiryBadge(expiresUtcIso, isExpired, isPermanent) {
        if (isPermanent) {
            return '<span title="Permanent">Permanent <span class="badge text-bg-secondary ms-1">Permanent</span></span>';
        }
        if (!expiresUtcIso) return '';

        var dateStr = formatDate(expiresUtcIso);
        if (isExpired) {
            return '<span title="Expired on ' + escapeAttr(dateStr) + '">' + dateStr +
                ' <span class="badge text-bg-danger ms-1">Expired</span></span>';
        }
        return '<span title="Expires on ' + escapeAttr(dateStr) + '">' + dateStr +
            ' <span class="badge text-bg-success ms-1">Active</span></span>';
    }

    /**
     * Compute relative time using Intl.RelativeTimeFormat where available,
     * with a plain-English fallback.
     */
    function computeRelativeTime(date) {
        var diffMs = Date.now() - date.getTime();
        var future = diffMs < 0;
        var absDiff = Math.abs(diffMs);
        var seconds = Math.round(absDiff / 1000);
        var minutes = Math.round(absDiff / 60000);
        var hours = Math.round(absDiff / 3600000);
        var days = Math.round(absDiff / 86400000);

        if (typeof Intl !== 'undefined' && Intl.RelativeTimeFormat) {
            try {
                var rtf = new Intl.RelativeTimeFormat(getLocale(), { numeric: 'auto' });
                var value, unit;
                if (seconds < 45) return rtf.format(future ? seconds : -seconds, 'second');
                if (minutes < 45) { value = minutes; unit = 'minute'; }
                else if (hours < 24) { value = hours; unit = 'hour'; }
                else if (days < 30) { value = days; unit = 'day'; }
                else if (days < 365) { value = Math.round(days / 30); unit = 'month'; }
                else { value = Math.round(days / 365); unit = 'year'; }
                return rtf.format(future ? value : -value, unit);
            } catch (e) { /* fall through to manual fallback */ }
        }

        if (seconds < 45) return future ? 'in a few seconds' : 'just now';
        if (seconds < 90) return future ? 'in a minute' : 'a minute ago';
        if (minutes < 45) return future ? 'in ' + minutes + ' minutes' : minutes + ' minutes ago';
        if (minutes < 90) return future ? 'in an hour' : 'an hour ago';
        if (hours < 24) return future ? 'in ' + hours + ' hours' : hours + ' hours ago';
        if (hours < 42) return future ? 'in a day' : 'a day ago';
        if (days < 30) return future ? 'in ' + days + ' days' : days + ' days ago';
        if (days < 45) return future ? 'in a month' : 'a month ago';
        if (days < 365) return future ? 'in ' + Math.round(days / 30) + ' months' : Math.round(days / 30) + ' months ago';
        if (days < 545) return future ? 'in a year' : 'a year ago';
        return future ? 'in ' + Math.round(days / 365) + ' years' : Math.round(days / 365) + ' years ago';
    }

    /**
     * Escape a string for safe use inside an HTML attribute value.
     */
    function escapeAttr(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    /**
     * Enhance all <time> elements with a data-dt attribute.
     * Call on DOMContentLoaded or after dynamic content insertion.
     * @param {Element} [root] - scope element (defaults to document)
     */
    function enhanceDateElements(root) {
        var container = root || document;
        var elements = container.querySelectorAll('time[data-dt]');

        for (var i = 0; i < elements.length; i++) {
            var el = elements[i];
            var iso = el.getAttribute('datetime');
            if (!iso) continue;

            var mode = el.getAttribute('data-dt');

            if (mode === 'relative') {
                var relative = formatRelativeTime(iso);
                var absolute = formatDateTime(iso);
                if (relative) {
                    el.textContent = relative;
                    el.setAttribute('title', absolute);
                }
            } else if (mode === 'localized') {
                var localized = formatDateTime(iso);
                if (localized) {
                    el.textContent = localized;
                }
            } else if (mode === 'expiry') {
                var status = el.getAttribute('data-dt-status');
                if (status === 'permanent') continue; // keep server-rendered content

                var dateFormatted = formatDate(iso);
                if (dateFormatted) {
                    var badge = el.querySelector('.badge');
                    if (badge) {
                        var badgeHtml = badge.outerHTML;
                        var isExp = status === 'expired';
                        var title = isExp ? 'Expired on ' + dateFormatted : 'Expires on ' + dateFormatted;
                        el.innerHTML = dateFormatted + ' ' + badgeHtml;
                        el.setAttribute('title', title);
                    }
                }
            }
        }
    }

    // Auto-enhance on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { enhanceDateElements(); });
    } else {
        enhanceDateElements();
    }

    return {
        getLocale: getLocale,
        getTimezone: getTimezone,
        parseUtc: parseUtc,
        formatDateTime: formatDateTime,
        formatRelativeTime: formatRelativeTime,
        formatDate: formatDate,
        formatExpiryBadge: formatExpiryBadge,
        enhanceDateElements: enhanceDateElements
    };
})();
