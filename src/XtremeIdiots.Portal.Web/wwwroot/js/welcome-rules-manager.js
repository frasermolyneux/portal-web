/* Shared welcome-rule manager used by both Global Settings and Game Server Edit. */
(function () {
    'use strict';

    function generateGuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }

        // Fallback for older browsers that do not support crypto.randomUUID.
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (character) {
            var random = Math.random() * 16 | 0;
            var value = character === 'x' ? random : (random & 0x3 | 0x8);
            return value.toString(16);
        });
    }

    function ensureRuleId(row, autoGenerateId) {
        if (!autoGenerateId) {
            return;
        }

        var idField = row.querySelector('[data-field="id"]');
        if (!idField || idField.tagName === 'SELECT') {
            return;
        }

        var current = (idField.value || '').trim();
        idField.value = current.length > 0 ? current : generateGuid();
    }

    function updateRuleCharCount(row) {
        var messageTemplate = row.querySelector('[data-field="message-template"]');
        var charCount = row.querySelector('[data-field="char-count"]');
        if (!messageTemplate || !charCount) return;

        charCount.textContent = (messageTemplate.value || '').length.toString();
    }

    function updateRequiredTagsOverride(row) {
        var overrideToggle = row.querySelector('[data-field="required-tags-override"]');
        var tagsWrapper = row.querySelector('[data-field="required-tags-wrapper"]');
        var overrideHidden = row.querySelector('[data-field="required-tags-override-hidden"]');

        if (overrideHidden && overrideToggle) {
            overrideHidden.disabled = overrideToggle.checked;
        }

        if (overrideToggle && tagsWrapper) {
            tagsWrapper.classList.toggle('d-none', !overrideToggle.checked);
        }
    }

    function initializeRequiredTagsSelectors(root) {
        if (!window.XIRequiredTagsSelector || typeof window.XIRequiredTagsSelector.initializeWithin !== 'function') {
            return;
        }

        window.XIRequiredTagsSelector.initializeWithin(root);
    }

    function updateEmptyState(container) {
        var emptyStateId = container.dataset.emptyStateId;
        if (!emptyStateId) return;

        var emptyState = document.getElementById(emptyStateId);
        if (!emptyState) return;

        var rows = container.querySelectorAll('[data-welcome-rule-row]').length;
        emptyState.classList.toggle('d-none', rows > 0);
    }

    function reindex(container) {
        var fieldNamePrefix = container.dataset.fieldNamePrefix;
        var validationPrefix = container.dataset.validationPrefix || fieldNamePrefix;
        var autoGenerateId = container.dataset.autoGenerateId === 'true';
        var normalizedPrefix = fieldNamePrefix.replaceAll('.', '_').replaceAll('[', '_').replaceAll(']', '');
        var priorityMode = container.dataset.priorityMode || 'none';
        var startPriority = parseInt(container.dataset.priorityStart || '1000', 10);
        var rows = Array.from(container.querySelectorAll('[data-welcome-rule-row]'));

        rows.forEach(function (row, index) {
            ensureRuleId(row, autoGenerateId);

            var id = row.querySelector('[data-field="id"]');
            var priority = row.querySelector('[data-field="priority"]');
            var visibility = row.querySelector('[data-field="visibility"]');
            var messageTemplate = row.querySelector('[data-field="message-template"]');
            var requiredTags = row.querySelector('[data-field="required-tags"]');
            var requiredTagsSelect = row.querySelector('[data-field="required-tags-select"]');
            var requiredTagsLabel = row.querySelector('[data-field="required-tags-label"]');
            var requiredTagsHelp = row.querySelector('[data-field="required-tags-help"]');
            var connectionDelay = row.querySelector('[data-field="connection-delay"]');
            var enabledHidden = row.querySelector('[data-field="enabled-hidden"]');
            var enabled = row.querySelector('[data-field="enabled"]');
            var enabledLabel = row.querySelector('[data-field="enabled-label"]');
            var enabledOverride = row.querySelector('[data-field="enabled-override"]');
            var requiredTagsOverride = row.querySelector('[data-field="required-tags-override"]');
            var requiredTagsOverrideLabel = row.querySelector('[data-field="required-tags-override-label"]');
            var requiredTagsOverrideHidden = row.querySelector('[data-field="required-tags-override-hidden"]');

            if (id) {
                id.name = fieldNamePrefix + '[' + index + '].Id';
                id.id = normalizedPrefix + '_' + index + '__Id';
            }

            if (priority) {
                priority.name = fieldNamePrefix + '[' + index + '].Priority';
                priority.id = normalizedPrefix + '_' + index + '__Priority';
                if (priorityMode === 'descending') {
                    priority.value = (startPriority - index).toString();
                }
            }

            if (visibility) {
                visibility.name = fieldNamePrefix + '[' + index + '].Visibility';
                visibility.id = normalizedPrefix + '_' + index + '__Visibility';
            }

            if (messageTemplate) {
                messageTemplate.name = fieldNamePrefix + '[' + index + '].MessageTemplate';
                messageTemplate.id = normalizedPrefix + '_' + index + '__MessageTemplate';
            }

            if (requiredTags) {
                requiredTags.name = fieldNamePrefix + '[' + index + '].RequiredTagsCsv';
                requiredTags.id = normalizedPrefix + '_' + index + '__RequiredTagsCsv';
            }

            if (requiredTagsSelect) {
                if (requiredTags && requiredTags.id) {
                    requiredTagsSelect.id = requiredTags.id + '_Selector';
                } else {
                    requiredTagsSelect.id = normalizedPrefix + '_' + index + '__RequiredTagsCsv_Selector';
                }
            }

            if (requiredTagsLabel && requiredTagsSelect) {
                requiredTagsLabel.htmlFor = requiredTagsSelect.id;
            }

            if (requiredTagsHelp) {
                if (requiredTags && requiredTags.id) {
                    requiredTagsHelp.id = requiredTags.id + '_Help';
                } else {
                    requiredTagsHelp.id = normalizedPrefix + '_' + index + '__RequiredTagsCsv_Help';
                }
            }

            if (requiredTagsSelect) {
                if (requiredTagsHelp) {
                    requiredTagsSelect.setAttribute('aria-describedby', requiredTagsHelp.id);
                } else {
                    requiredTagsSelect.removeAttribute('aria-describedby');
                }
            }

            if (connectionDelay) {
                connectionDelay.name = fieldNamePrefix + '[' + index + '].ConnectionDelaySeconds';
                connectionDelay.id = normalizedPrefix + '_' + index + '__ConnectionDelaySeconds';
            }

            if (enabledHidden) {
                enabledHidden.name = fieldNamePrefix + '[' + index + '].Enabled';
            }

            if (enabled) {
                enabled.name = fieldNamePrefix + '[' + index + '].Enabled';
                enabled.id = normalizedPrefix + '_' + index + '__Enabled';
                if (enabledHidden) {
                    enabledHidden.disabled = enabled.checked;
                }
            }

            if (enabledLabel && enabled) {
                enabledLabel.htmlFor = enabled.id;
            }

            if (enabledOverride) {
                enabledOverride.name = fieldNamePrefix + '[' + index + '].Enabled';
                enabledOverride.id = normalizedPrefix + '_' + index + '__Enabled';
            }

            if (requiredTagsOverrideHidden) {
                requiredTagsOverrideHidden.name = fieldNamePrefix + '[' + index + '].OverrideRequiredTags';
            }

            if (requiredTagsOverride) {
                requiredTagsOverride.name = fieldNamePrefix + '[' + index + '].OverrideRequiredTags';
                requiredTagsOverride.id = normalizedPrefix + '_' + index + '__OverrideRequiredTags';
                if (requiredTagsOverrideHidden) {
                    requiredTagsOverrideHidden.disabled = requiredTagsOverride.checked;
                }
            }

            if (requiredTagsOverrideLabel && requiredTagsOverride) {
                requiredTagsOverrideLabel.htmlFor = requiredTagsOverride.id;
            }

            var idValidation = row.querySelector('[data-field="id-validation"]');
            if (idValidation) {
                idValidation.setAttribute('data-valmsg-for', validationPrefix + '[' + index + '].Id');
            }

            var priorityValidation = row.querySelector('[data-field="priority-validation"]');
            if (priorityValidation) {
                priorityValidation.setAttribute('data-valmsg-for', validationPrefix + '[' + index + '].Priority');
            }

            var messageTemplateValidation = row.querySelector('[data-field="message-template-validation"]');
            if (messageTemplateValidation) {
                messageTemplateValidation.setAttribute('data-valmsg-for', validationPrefix + '[' + index + '].MessageTemplate');
            }

            var requiredTagsValidation = row.querySelector('[data-field="required-tags-validation"]');
            if (requiredTagsValidation) {
                requiredTagsValidation.setAttribute('data-valmsg-for', validationPrefix + '[' + index + '].RequiredTagsCsv');
            }

            var connectionDelayValidation = row.querySelector('[data-field="connection-delay-validation"]');
            if (connectionDelayValidation) {
                connectionDelayValidation.setAttribute('data-valmsg-for', validationPrefix + '[' + index + '].ConnectionDelaySeconds');
            }

            updateRuleCharCount(row);
            updateRequiredTagsOverride(row);
        });

        initializeRequiredTagsSelectors(container);
    }

    function wireRow(row, container, autoGenerateId) {
        var moveUpButton = row.querySelector('[data-action="move-up"]');
        var moveDownButton = row.querySelector('[data-action="move-down"]');
        var removeButton = row.querySelector('[data-action="remove"]');
        var messageTemplate = row.querySelector('[data-field="message-template"]');
        var enabled = row.querySelector('[data-field="enabled"]');
        var enabledHidden = row.querySelector('[data-field="enabled-hidden"]');
        var requiredTagsOverride = row.querySelector('[data-field="required-tags-override"]');

        if (moveUpButton) {
            moveUpButton.addEventListener('click', function () {
                var previous = row.previousElementSibling;
                if (!previous) return;

                container.insertBefore(row, previous);
                reindex(container);
            });
        }

        if (moveDownButton) {
            moveDownButton.addEventListener('click', function () {
                var next = row.nextElementSibling;
                if (!next) return;

                container.insertBefore(next, row);
                reindex(container);
            });
        }

        if (removeButton) {
            removeButton.addEventListener('click', function () {
                row.remove();
                reindex(container);
                updateEmptyState(container);
            });
        }

        if (messageTemplate) {
            messageTemplate.addEventListener('input', function () {
                updateRuleCharCount(row);
            });
        }

        if (enabled && enabledHidden) {
            enabled.addEventListener('change', function () {
                enabledHidden.disabled = enabled.checked;
            });
        }

        if (requiredTagsOverride) {
            requiredTagsOverride.addEventListener('change', function () {
                updateRequiredTagsOverride(row);
            });
        }

        ensureRuleId(row, autoGenerateId);
        updateRuleCharCount(row);
        updateRequiredTagsOverride(row);
        initializeRequiredTagsSelectors(row);
    }

    function initializeEditor(containerId, addButtonId) {
        var container = document.getElementById(containerId);
        if (!container) return;

        var autoGenerateId = container.dataset.autoGenerateId === 'true';

        var templateId = container.dataset.templateId;
        var template = document.getElementById(templateId);
        if (!template) return;

        container.querySelectorAll('[data-welcome-rule-row]').forEach(function (row) {
            wireRow(row, container, autoGenerateId);
        });

        var addButton = document.getElementById(addButtonId);
        if (addButton) {
            addButton.addEventListener('click', function () {
                var row = template.content.firstElementChild.cloneNode(true);
                wireRow(row, container, autoGenerateId);
                container.appendChild(row);
                reindex(container);
                updateEmptyState(container);
            });
        }

        reindex(container);
        updateEmptyState(container);

        var form = container.closest('form');
        if (form) {
            form.addEventListener('submit', function () {
                reindex(container);
            });
        }
    }

    window.XIWelcomeRulesManager = {
        initializeEditor: initializeEditor
    };
})();
