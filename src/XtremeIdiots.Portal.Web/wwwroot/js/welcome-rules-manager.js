/* Shared welcome-rule manager used by both Global Settings and Game Server Edit. */
(function () {
    'use strict';

    var TOKEN_REGEX = /\{([a-zA-Z0-9]+)\}/g;

    function getTokenCatalog() {
        if (window.__welcomeTokenCatalog) {
            return window.__welcomeTokenCatalog;
        }

        var element = document.getElementById('welcome-token-catalog');
        var catalog = [];
        if (element) {
            try {
                catalog = JSON.parse(element.textContent || '[]') || [];
            } catch (error) {
                catalog = [];
            }
        }

        window.__welcomeTokenCatalog = catalog;
        return catalog;
    }

    function getSampleMap() {
        if (window.__welcomeSampleMap) {
            return window.__welcomeSampleMap;
        }

        var map = {};
        getTokenCatalog().forEach(function (token) {
            if (token && token.key) {
                map[String(token.key).toLowerCase()] = token.sampleValue != null ? String(token.sampleValue) : '';
            }
        });

        window.__welcomeSampleMap = map;
        return map;
    }

    function fallbackEscape(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function renderPreviewHtml(template) {
        var samples = getSampleMap();
        // Single-pass replacement of known tokens with sample values; unknown tokens are left intact.
        // Sample values are never re-scanned, mirroring the backend renderer.
        var raw = String(template || '').replace(TOKEN_REGEX, function (match, key) {
            var lower = String(key).toLowerCase();
            return Object.prototype.hasOwnProperty.call(samples, lower) ? samples[lower] : match;
        });

        if (typeof CodColors !== 'undefined' && CodColors && typeof CodColors.renderSafe === 'function') {
            return CodColors.renderSafe(raw);
        }

        return fallbackEscape(raw);
    }

    function updateRulePreview(row) {
        var messageTemplate = row.querySelector('[data-field="message-template"]');
        var preview = row.querySelector('[data-field="message-preview"]');
        if (!messageTemplate || !preview) return;

        var value = messageTemplate.value || '';
        preview.innerHTML = renderPreviewHtml(value);
        preview.classList.toggle('is-empty', value.trim().length === 0);
    }

    function insertTokenAtCursor(textarea, token) {
        if (!textarea || !token) return;

        var start = typeof textarea.selectionStart === 'number' ? textarea.selectionStart : textarea.value.length;
        var end = typeof textarea.selectionEnd === 'number' ? textarea.selectionEnd : textarea.value.length;
        var before = textarea.value.substring(0, start);
        var after = textarea.value.substring(end);

        textarea.value = before + token + after;

        var caret = start + token.length;
        textarea.focus();
        try {
            textarea.setSelectionRange(caret, caret);
        } catch (error) {
            /* setSelectionRange unsupported — ignore */
        }

        textarea.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function buildComposer() {
        var composer = document.createElement('div');
        composer.className = 'welcome-composer mt-2';
        composer.setAttribute('data-field', 'token-help');

        var chips = document.createElement('div');
        chips.className = 'welcome-token-chips';
        chips.setAttribute('role', 'group');
        chips.setAttribute('aria-label', 'Insert welcome message token');

        var chipsLabel = document.createElement('span');
        chipsLabel.className = 'form-text text-muted me-1 align-self-center';
        chipsLabel.textContent = 'Insert token:';
        chips.appendChild(chipsLabel);

        getTokenCatalog().forEach(function (token) {
            if (!token || !token.token) return;

            var chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'btn btn-outline-secondary btn-sm welcome-token-chip';
            chip.setAttribute('data-token-insert', token.token);
            chip.textContent = token.token;

            if (token.displayName || token.description) {
                chip.title = (token.displayName || '') + (token.description ? ' — ' + token.description : '');
            }

            chips.appendChild(chip);
        });

        composer.appendChild(chips);

        var previewWrapper = document.createElement('div');
        previewWrapper.className = 'mt-2';

        var previewLabel = document.createElement('span');
        previewLabel.className = 'form-text text-muted d-block mb-1';
        previewLabel.textContent = 'Preview';

        var preview = document.createElement('div');
        preview.className = 'welcome-message-preview';
        preview.setAttribute('data-field', 'message-preview');
        preview.setAttribute('aria-live', 'polite');

        previewWrapper.appendChild(previewLabel);
        previewWrapper.appendChild(preview);
        composer.appendChild(previewWrapper);

        return composer;
    }

    function ensureComposer(row) {
        var messageTemplate = row.querySelector('[data-field="message-template"]');
        if (!messageTemplate) return;
        if (row.querySelector('[data-field="token-help"]')) return;

        var container = messageTemplate.closest('.mb-3') || messageTemplate.parentElement;
        if (!container) return;

        var composer = buildComposer();
        container.appendChild(composer);

        composer.querySelectorAll('[data-token-insert]').forEach(function (chip) {
            chip.addEventListener('click', function () {
                insertTokenAtCursor(messageTemplate, chip.getAttribute('data-token-insert'));
            });
        });
    }

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
                updateRulePreview(row);
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
        ensureComposer(row);
        updateRuleCharCount(row);
        updateRulePreview(row);
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
