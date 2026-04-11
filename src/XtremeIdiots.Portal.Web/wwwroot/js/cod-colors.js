/**
 * CoD / Quake Color Code Renderer
 * Converts ^0–^9 color codes into colored HTML spans.
 *
 * IMPORTANT: Input text MUST be HTML-escaped before calling renderCodColors()
 * to prevent XSS. Use escapeHtml() first, then pass the result here.
 *
 * Usage:
 *   var safe = escapeHtml(rawText);
 *   var html = renderCodColors(safe);
 *   element.innerHTML = html;
 */
var CodColors = (function () {
    'use strict';

    var COLOR_REGEX = /\^([0-9])/g;

    /**
     * Renders CoD color codes in pre-escaped HTML text.
     * @param {string} escapedText - HTML-escaped text containing ^0–^9 codes
     * @returns {string} HTML with colored spans
     */
    function render(escapedText) {
        if (!escapedText || typeof escapedText !== 'string') return escapedText || '';
        if (escapedText.indexOf('^') === -1) return escapedText;

        var result = '';
        var currentColor = null;
        var lastIndex = 0;

        COLOR_REGEX.lastIndex = 0;
        var match;
        while ((match = COLOR_REGEX.exec(escapedText)) !== null) {
            // Append text before this color code
            var textBefore = escapedText.substring(lastIndex, match.index);
            if (textBefore) {
                if (currentColor !== null) {
                    result += '<span class="cod-color-' + currentColor + '">' + textBefore + '</span>';
                } else {
                    result += textBefore;
                }
            }

            currentColor = match[1];
            lastIndex = match.index + match[0].length;
        }

        // Append remaining text after the last color code
        var remaining = escapedText.substring(lastIndex);
        if (remaining) {
            if (currentColor !== null) {
                result += '<span class="cod-color-' + currentColor + '">' + remaining + '</span>';
            } else {
                result += remaining;
            }
        }

        return result.length > 0 ? '<span class="cod-colored">' + result + '</span>' : escapedText;
    }

    /**
     * Convenience: escapes HTML then renders color codes.
     * @param {string} rawText - Raw unescaped text
     * @returns {string} Safe HTML with colored spans
     */
    function renderSafe(rawText) {
        if (!rawText || typeof rawText !== 'string') return rawText || '';
        if (typeof escapeHtml === 'function') {
            return render(escapeHtml(rawText));
        }
        // Fallback inline escape if global escapeHtml not available
        var escaped = rawText
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
        return render(escaped);
    }

    return {
        render: render,
        renderSafe: renderSafe
    };
})();

// Global shorthand
function renderCodColors(escapedText) {
    return CodColors.render(escapedText);
}
