var PlayerTags = (function () {
    'use strict';

    function isRenderableTagHtml(value) {
        if (typeof value !== 'string') return false;
        var sanitizedPattern = /^<span\s+class="[a-zA-Z0-9_\- ]+">(?:<i\s+class="[a-zA-Z0-9_\- ]+"><\/i>\s*)?[^<>]+<\/span>$/;
        return sanitizedPattern.test(value.trim());
    }

    function render(tags, type) {
        if (!Array.isArray(tags) || tags.length === 0) {
            return type === 'display' ? '<span class="text-muted">-</span>' : '';
        }
        if (type !== 'display') {
            return tags.map(function (tag) { return tag?.name || ''; })
                .filter(function (name) { return name.length > 0; }).join(', ');
        }
        var maxVisibleTags = 3;
        var visibleTagHtml = tags.slice(0, maxVisibleTags).map(function (tag) {
            if (isRenderableTagHtml(tag?.tagHtml)) {
                return '<span class="me-1">' + tag.tagHtml + '</span>';
            }
            return tag?.name ? '<span class="badge bg-secondary me-1">' + escapeHtml(tag.name) + '</span>' : '';
        }).join('');
        var remainingTagCount = tags.length - maxVisibleTags;
        var overflow = remainingTagCount > 0
            ? '<span class="badge bg-light text-dark border">+' + remainingTagCount + '</span>' : '';
        return '<div class="d-flex flex-wrap align-items-center">' + visibleTagHtml + overflow + '</div>';
    }

    function escapeHtml(value) {
        return String(value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    return { render: render };
})();
