/* Shared message-row manager for settings pages (global + server). */
(function () {
    'use strict';

    function getMessageRowIdPrefix(fieldNamePrefix, normalizeIdPrefix) {
        if (normalizeIdPrefix) {
            return fieldNamePrefix.replaceAll('.', '_').replaceAll('[', '_').replaceAll(']', '');
        }

        return fieldNamePrefix;
    }

    function stripCodColorCodes(text) {
        return (text || '').replace(/\^[0-9]/g, '');
    }

    function renderCodPreview(previewElement, text) {
        var colors = {
            '1': '#ff4d4f',
            '2': '#52c41a',
            '3': '#ffd666',
            '4': '#40a9ff',
            '5': '#69c0ff',
            '6': '#fa8c16',
            '7': '#f0f0f0',
            '8': '#000000',
            '9': '#ff85c0',
            '0': '#ffffff'
        };

        previewElement.textContent = '';

        var currentColor = colors['0'];
        var lastIndex = 0;
        var regex = /\^([0-9])/g;
        var match;

        while ((match = regex.exec(text)) !== null) {
            var segment = text.substring(lastIndex, match.index);
            if (segment.length) {
                var span = document.createElement('span');
                span.style.color = currentColor;
                span.textContent = segment;
                previewElement.appendChild(span);
            }

            currentColor = colors[match[1]] || currentColor;
            lastIndex = regex.lastIndex;
        }

        var tail = text.substring(lastIndex);
        if (tail.length) {
            var tailSpan = document.createElement('span');
            tailSpan.style.color = currentColor;
            tailSpan.textContent = tail;
            previewElement.appendChild(tailSpan);
        }
    }

    function updateRowPreview(row, options) {
        var input = row.querySelector('[data-field="message"]');
        var preview = row.querySelector('[data-field="preview"]');
        var count = row.querySelector('[data-field="char-count"]');
        if (!input || !preview || !count) return;

        var value = input.value || '';
        count.textContent = value.length.toString();

        var previewText = options.replaceNameToken
            ? value.replaceAll('{name}', 'PlayerName')
            : value;

        var agentPrefix = options.agentNameProvider ? options.agentNameProvider() : '';
        if (agentPrefix.length > 0) {
            previewText = previewText.length > 0 ? (agentPrefix + ' ' + previewText) : agentPrefix;
        }

        if (!previewText.length) {
            preview.textContent = '(preview)';
            return;
        }

        if (options.codPreviewMode === 'always') {
            renderCodPreview(preview, previewText);
            return;
        }

        if (options.codPreviewMode === 'conditional') {
            var shouldRenderCod = options.isCodGameType ? options.isCodGameType() : false;
            if (shouldRenderCod) {
                renderCodPreview(preview, previewText);
            } else {
                preview.textContent = stripCodColorCodes(previewText);
            }

            return;
        }

        preview.textContent = previewText;
    }

    function updateMessageEmptyState(container, emptyStateId) {
        var emptyState = document.getElementById(emptyStateId);
        if (!emptyState) return;

        emptyState.classList.toggle('d-none', container.querySelectorAll('[data-message-row]').length > 0);
    }

    function reindexRowsByFieldPrefix(container, fieldNamePrefix, normalizeIdPrefix) {
        var idPrefix = getMessageRowIdPrefix(fieldNamePrefix, normalizeIdPrefix);

        container.querySelectorAll('[data-message-row]').forEach(function (row, index) {
            row.dataset.index = index.toString();

            var enabledHidden = row.querySelector('[data-field="enabled-hidden"]');
            var enabled = row.querySelector('[data-field="enabled"]');
            var enabledLabel = row.querySelector('[data-field="enabled-label"]');
            var message = row.querySelector('[data-field="message"]');
            var validation = row.querySelector('[data-field="validation"]');

            if (enabledHidden) enabledHidden.name = fieldNamePrefix + '[' + index + '].Enabled';
            if (enabled) {
                enabled.name = fieldNamePrefix + '[' + index + '].Enabled';
                enabled.id = idPrefix + '_' + index + '__Enabled';
            }
            if (enabledLabel && enabled) enabledLabel.htmlFor = enabled.id;
            if (message) {
                message.name = fieldNamePrefix + '[' + index + '].Message';
                message.id = idPrefix + '_' + index + '__Message';
            }
            if (validation) {
                validation.setAttribute('data-valmsg-for', fieldNamePrefix + '[' + index + '].Message');
            }
        });
    }

    function syncRowEnabledState(row) {
        var enabledHidden = row.querySelector('[data-field="enabled-hidden"]');
        var enabled = row.querySelector('[data-field="enabled"]');
        if (!enabledHidden || !enabled) return;

        enabledHidden.disabled = enabled.checked;
    }

    function buildImportedMessages(rawText) {
        if (!rawText || !rawText.length) {
            return [];
        }

        return rawText
            .split(/\r?\n/)
            .map(function (line) {
                return line.trim();
            })
            .filter(function (line) {
                return line.length > 0;
            });
    }

    function showMultilineImportDialog(title, placeholder, onConfirm, options) {
        var settings = options || {};
        var dialogLabelId = settings.dialogLabelId || 'settings-row-manager-import-title';
        var textareaId = settings.textareaId || 'settings-row-manager-import-textarea';
        var triggerElement = settings.triggerElement || null;

        if (typeof HTMLDialogElement === 'undefined') {
            var fallbackInput = window.prompt(title + '\n\nPaste one message per line:', '');
            if (fallbackInput === null) {
                return;
            }

            onConfirm(fallbackInput);
            return;
        }

        var dialog = document.createElement('dialog');
        dialog.className = 'rounded border p-0';
        dialog.setAttribute('aria-labelledby', dialogLabelId);

        dialog.innerHTML = [
            '<form method="dialog" class="p-3" style="min-width: min(640px, 90vw);">',
            '<h6 class="mb-2" id="' + dialogLabelId + '">' + title + '</h6>',
            '<p class="text-muted mb-2">Paste one message per line. Empty lines are ignored.</p>',
            '<label class="form-label" for="' + textareaId + '">Messages</label>',
            '<textarea class="form-control mb-3" id="' + textareaId + '" rows="10" placeholder="' + placeholder + '"></textarea>',
            '<div class="d-flex justify-content-end gap-2">',
            '<button type="button" class="btn btn-outline-secondary" data-action="cancel">Cancel</button>',
            '<button type="button" class="btn btn-primary" data-action="import">Import</button>',
            '</div>',
            '</form>'
        ].join('');

        document.body.appendChild(dialog);

        var textarea = dialog.querySelector('textarea');
        var cancelButton = dialog.querySelector('[data-action="cancel"]');
        var importButton = dialog.querySelector('[data-action="import"]');

        function closeDialog() {
            dialog.close();
            dialog.remove();
            if (triggerElement && typeof triggerElement.focus === 'function') {
                triggerElement.focus();
            }
        }

        cancelButton.addEventListener('click', closeDialog);
        importButton.addEventListener('click', function () {
            onConfirm(textarea.value || '');
            closeDialog();
        });

        dialog.addEventListener('cancel', function () {
            closeDialog();
        }, { once: true });

        dialog.showModal();
        textarea.focus();
    }

    function appendMessageRow(template, settings, messageValue) {
        var row = template.content.firstElementChild.cloneNode(true);
        var messageInput = row.querySelector('[data-field="message"]');

        if (messageInput) {
            messageInput.value = messageValue || '';
        }

        wireSettingsRow(row, settings);
        settings.container.appendChild(row);
    }

    function wireSettingsRow(row, options) {
        var removeButton = row.querySelector('[data-action="remove"]');
        var moveUpButton = row.querySelector('[data-action="move-up"]');
        var moveDownButton = row.querySelector('[data-action="move-down"]');
        var messageInput = row.querySelector('[data-field="message"]');
        var enabled = row.querySelector('[data-field="enabled"]');

        if (removeButton) {
            removeButton.addEventListener('click', function () {
                row.remove();
                reindexRowsByFieldPrefix(options.container, options.fieldNamePrefix, options.normalizeIdPrefix);
                updateMessageEmptyState(options.container, options.emptyStateId);
            });
        }

        if (moveUpButton) {
            moveUpButton.addEventListener('click', function () {
                var previous = row.previousElementSibling;
                if (!previous) return;

                options.container.insertBefore(row, previous);
                reindexRowsByFieldPrefix(options.container, options.fieldNamePrefix, options.normalizeIdPrefix);
            });
        }

        if (moveDownButton) {
            moveDownButton.addEventListener('click', function () {
                var next = row.nextElementSibling;
                if (!next) return;

                options.container.insertBefore(next, row);
                reindexRowsByFieldPrefix(options.container, options.fieldNamePrefix, options.normalizeIdPrefix);
            });
        }

        if (messageInput) {
            messageInput.addEventListener('input', function () {
                updateRowPreview(row, options);
            });
        }

        if (enabled) {
            enabled.addEventListener('change', function () {
                syncRowEnabledState(row);
            });
        }

        updateRowPreview(row, options);
        syncRowEnabledState(row);
    }

    function initializeMessageList(options) {
        var container = options.container || document.getElementById(options.containerId);
        if (!container) return;

        var template = document.getElementById(options.templateId);
        if (!template) return;

        var settings = {
            container: container,
            fieldNamePrefix: options.fieldNamePrefix,
            emptyStateId: options.emptyStateId,
            normalizeIdPrefix: options.normalizeIdPrefix !== false,
            agentNameProvider: options.agentNameProvider,
            replaceNameToken: options.replaceNameToken === true,
            codPreviewMode: options.codPreviewMode || 'never',
            isCodGameType: options.isCodGameType
        };

        container.querySelectorAll('[data-message-row]').forEach(function (row) {
            wireSettingsRow(row, settings);
        });

        var addButton = document.getElementById(options.addButtonId);
        if (addButton) {
            addButton.addEventListener('click', function () {
                appendMessageRow(template, settings, '');
                reindexRowsByFieldPrefix(container, settings.fieldNamePrefix, settings.normalizeIdPrefix);
                updateMessageEmptyState(container, settings.emptyStateId);
            });
        }

        var importButtonId = options.importButtonId;
        var importButton = importButtonId ? document.getElementById(importButtonId) : null;
        var maxImportRows = Number.isFinite(options.maxImportRows) && options.maxImportRows > 0
            ? Math.floor(options.maxImportRows)
            : 200;
        if (importButton) {
            importButton.addEventListener('click', function () {
                showMultilineImportDialog(
                    'Import command messages',
                    'Example: Hello {name}!',
                    function (rawText) {
                        var importedMessages = buildImportedMessages(rawText);
                        if (importedMessages.length === 0) {
                            return;
                        }

                        if (importedMessages.length > maxImportRows) {
                            importedMessages = importedMessages.slice(0, maxImportRows);
                        }

                        importedMessages.forEach(function (messageText) {
                            appendMessageRow(template, settings, messageText);
                        });

                        reindexRowsByFieldPrefix(container, settings.fieldNamePrefix, settings.normalizeIdPrefix);
                        updateMessageEmptyState(container, settings.emptyStateId);
                    }, {
                    dialogLabelId: container.id + '-import-title',
                    textareaId: container.id + '-import-textarea',
                    triggerElement: importButton
                });
            });
        }

        reindexRowsByFieldPrefix(container, settings.fieldNamePrefix, settings.normalizeIdPrefix);
        updateMessageEmptyState(container, settings.emptyStateId);

        if (options.agentNameInput) {
            options.agentNameInput.addEventListener('input', function () {
                container.querySelectorAll('[data-message-row]').forEach(function (row) {
                    updateRowPreview(row, settings);
                });
            });
        }

        return {
            container: container,
            reindex: function () {
                reindexRowsByFieldPrefix(container, settings.fieldNamePrefix, settings.normalizeIdPrefix);
            },
            syncBooleanPosts: function () {
                container.querySelectorAll('[data-message-row]').forEach(function (row) {
                    syncRowEnabledState(row);
                });
            }
        };
    }

    window.XISettingsRowManager = {
        initializeMessageList: initializeMessageList,
        reindexRowsByFieldPrefix: reindexRowsByFieldPrefix,
        wireSettingsRow: wireSettingsRow,
        updateRowPreview: updateRowPreview,
        renderCodPreview: renderCodPreview
    };
})();