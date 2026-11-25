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

    function segments(text) {
        var result = [];
        var currentColor = null;
        var lastIndex = 0;
        COLOR_REGEX.lastIndex = 0;
        var match;
        while ((match = COLOR_REGEX.exec(text)) !== null) {
            if (match.index > lastIndex) {
                result.push({ text: text.substring(lastIndex, match.index), color: currentColor });
            }
            currentColor = match[1];
            lastIndex = match.index + match[0].length;
        }
        if (lastIndex < text.length) {
            result.push({ text: text.substring(lastIndex), color: currentColor });
        }
        return result;
    }

    /**
     * Renders CoD color codes in pre-escaped HTML text.
     * @param {string} escapedText - HTML-escaped text containing ^0–^9 codes
     * @returns {string} HTML with colored spans
     */
    function render(escapedText) {
        if (!escapedText || typeof escapedText !== 'string') return escapedText || '';
        if (escapedText.indexOf('^') === -1) return escapedText;

        var parts = segments(escapedText);
        if (parts.length === 0) return escapedText;
        var result = parts.map(function (part) {
            return part.color === null ? part.text
                : '<span class="cod-color-' + part.color + '">' + part.text + '</span>';
        }).join('');
        return '<span class="cod-colored">' + result + '</span>';
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

    function renderInto(target, rawText) {
        if (!rawText || typeof rawText !== 'string' || rawText.indexOf('^') === -1) {
            target.textContent = rawText || '';
            return;
        }
        var parts = segments(rawText);
        if (parts.length === 0) {
            target.textContent = rawText;
            return;
        }
        var wrapper = document.createElement('span');
        wrapper.className = 'cod-colored';
        parts.forEach(function (part) {
            if (part.color === null) {
                wrapper.append(document.createTextNode(part.text));
            } else {
                var span = document.createElement('span');
                span.className = 'cod-color-' + part.color;
                span.textContent = part.text;
                wrapper.append(span);
            }
        });
        target.replaceChildren(wrapper);
    }

    return {
        render: render,
        renderSafe: renderSafe,
        renderInto: renderInto
    };
})();

// Global shorthand
function renderCodColors(escapedText) {
    return CodColors.render(escapedText);
}
