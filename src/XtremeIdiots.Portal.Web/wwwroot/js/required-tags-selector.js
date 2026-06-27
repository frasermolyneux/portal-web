(function () {
    'use strict';

    function splitCsv(value) {
        if (!value || !value.trim()) {
            return [];
        }

        var seen = new Set();
        return value
            .split(',')
            .map(function (item) { return item.trim(); })
            .filter(function (item) {
                if (!item) {
                    return false;
                }

                var normalized = item.toLowerCase();
                if (seen.has(normalized)) {
                    return false;
                }

                seen.add(normalized);
                return true;
            });
    }

    function toCsv(values) {
        return values.join(', ');
    }

    function getBadgeClass(tagName) {
        var classes = ['text-bg-primary', 'text-bg-success', 'text-bg-info', 'text-bg-warning', 'text-bg-danger', 'text-bg-secondary'];
        var hash = 0;

        for (var i = 0; i < tagName.length; i++) {
            hash = ((hash << 5) - hash) + tagName.charCodeAt(i);
            hash |= 0;
        }

        return classes[Math.abs(hash) % classes.length];
    }

    function renderSelectedTags(container, selectedValues, selectedLookup) {
        container.innerHTML = '';

        if (selectedValues.length === 0) {
            var placeholder = document.createElement('span');
            placeholder.className = 'text-muted small';
            placeholder.textContent = container.dataset.requiredTagsPlaceholder || 'No tags selected';
            container.appendChild(placeholder);
            return;
        }

        selectedValues.forEach(function (value) {
            var badge = document.createElement('span');
            badge.className = 'badge ' + getBadgeClass(value);
            badge.textContent = selectedLookup.get(value.toLowerCase()) || value;
            container.appendChild(badge);
        });
    }

    function getSelectedOptionValues(selectElement) {
        return Array.from(selectElement.selectedOptions).map(function (option) {
            return option.value;
        });
    }

    function syncFromHidden(hiddenInput, selectElement) {
        var selectedValues = splitCsv(hiddenInput.value);
        var selectedValueSet = new Set(selectedValues.map(function (value) { return value.toLowerCase(); }));

        Array.from(selectElement.options).forEach(function (option) {
            option.selected = selectedValueSet.has(option.value.toLowerCase());
        });
    }

    function initializeSelector(selectorRoot) {
        var hiddenInput = selectorRoot.querySelector('[data-required-tags-hidden]');
        var selectElement = selectorRoot.querySelector('[data-required-tags-select]');
        var selectedContainer = selectorRoot.querySelector('[data-required-tags-selected]');

        if (!hiddenInput || !selectElement || !selectedContainer) {
            return;
        }

        syncFromHidden(hiddenInput, selectElement);

        var selectedLookup = new Map(Array.from(selectElement.options).map(function (option) {
            return [option.value.toLowerCase(), option.textContent || option.value];
        }));

        var initialValues = splitCsv(hiddenInput.value);
        const preservedUnknownValues = initialValues.filter(function (value) {
            return !selectedLookup.has(value.toLowerCase());
        });

        function mergeValuesWithPreservedUnknowns(selectedValues, includeUnknownValues) {
            if (!includeUnknownValues || preservedUnknownValues.length === 0) {
                return selectedValues;
            }

            var merged = selectedValues.slice();
            var seen = new Set(merged.map(function (value) { return value.toLowerCase(); }));

            preservedUnknownValues.forEach(function (value) {
                var normalized = value.toLowerCase();
                if (!seen.has(normalized)) {
                    merged.push(value);
                    seen.add(normalized);
                }
            });

            return merged;
        }

        function syncToHiddenAndBadges(includeUnknownValues) {
            var selectedValues = getSelectedOptionValues(selectElement);
            var persistedValues = mergeValuesWithPreservedUnknowns(selectedValues, includeUnknownValues);

            if (!includeUnknownValues) {
                preservedUnknownValues.length = 0;
            }

            hiddenInput.value = toCsv(persistedValues);
            renderSelectedTags(selectedContainer, persistedValues, selectedLookup);
            hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
        }

        if (!selectorRoot.dataset.requiredTagsSelectorWired) {
            selectElement.addEventListener('change', function () {
                syncToHiddenAndBadges(false);
            });
            selectorRoot.dataset.requiredTagsSelectorWired = 'true';
        }

        syncToHiddenAndBadges(true);
    }

    function initializeWithin(root) {
        (root || document).querySelectorAll('[data-required-tags-selector]').forEach(function (selectorRoot) {
            initializeSelector(selectorRoot);
        });
    }

    window.XIRequiredTagsSelector = {
        initializeWithin: initializeWithin
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            initializeWithin(document);
        });
    } else {
        initializeWithin(document);
    }
})();
