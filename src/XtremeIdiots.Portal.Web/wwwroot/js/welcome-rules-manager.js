/**
 * Welcome Message Rules Manager
 * Handles adding, removing, reordering, and managing welcome message rules and overrides
 */

class WelcomeRulesManager {
    constructor() {
        this.initializeContainers();
    }

    initializeContainers() {
        // Find all rule containers on the page
        document.querySelectorAll('[id$="-welcome-rules-container"], [id$="-welcome-rule-overrides-container"]').forEach(container => {
            this.setupContainer(container);
        });
    }

    setupContainer(container) {
        const containerId = container.id;
        const addButtonId = `add-${containerId.replace('-container', '')}`;
        const addButton = document.getElementById(addButtonId);

        if (addButton) {
            addButton.addEventListener('click', () => this.addRule(container));
        }

        // Setup event listeners for existing rows
        this.setupRowEventListeners(container);
    }

    setupRowEventListeners(container) {
        container.querySelectorAll('[data-welcome-rule-row]').forEach(row => {
            this.setupRowButtons(container, row);
            this.setupOverrideCheckbox(row);
            this.setupCharacterCount(row);
        });
    }

    setupRowButtons(container, row) {
        const moveUpBtn = row.querySelector('[data-action="move-up"]');
        const moveDownBtn = row.querySelector('[data-action="move-down"]');
        const removeBtn = row.querySelector('[data-action="remove"]');

        if (moveUpBtn) {
            moveUpBtn.addEventListener('click', () => this.moveRuleUp(container, row));
        }
        if (moveDownBtn) {
            moveDownBtn.addEventListener('click', () => this.moveRuleDown(container, row));
        }
        if (removeBtn) {
            removeBtn.addEventListener('click', () => this.removeRule(container, row));
        }
    }

    setupOverrideCheckbox(row) {
        const checkbox = row.querySelector('[data-field="required-tags-override"]');
        const wrapper = row.querySelector('[data-field="required-tags-wrapper"]');

        if (checkbox && wrapper) {
            checkbox.addEventListener('change', () => {
                if (checkbox.checked) {
                    wrapper.classList.remove('d-none');
                } else {
                    wrapper.classList.add('d-none');
                }
            });
        }
    }

    setupCharacterCount(row) {
        const textarea = row.querySelector('[data-field="message-template"]');
        const countSpan = row.querySelector('[data-field="char-count"]');

        if (textarea && countSpan) {
            const updateCount = () => {
                countSpan.textContent = textarea.value.length;
            };

            textarea.addEventListener('input', updateCount);
            updateCount();
        }
    }

    addRule(container) {
        const templateId = container.dataset.templateId;
        const template = document.getElementById(templateId);

        if (!template) {
            console.error(`Template with id "${templateId}" not found`);
            return;
        }

        const newRow = template.content.cloneNode(true);
        const newRowElement = newRow.querySelector('[data-welcome-rule-row]');

        container.appendChild(newRowElement);
        this.setupRowEventListeners(container);
        this.reindexFields(container);
        this.updateEmptyState(container);
    }

    moveRuleUp(container, row) {
        const previousRow = row.previousElementSibling;

        if (previousRow && previousRow.dataset.welcomeRuleRow !== undefined) {
            row.parentNode.insertBefore(row, previousRow);
            this.reindexFields(container);
        }
    }

    moveRuleDown(container, row) {
        const nextRow = row.nextElementSibling;

        if (nextRow && nextRow.dataset.welcomeRuleRow !== undefined) {
            row.parentNode.insertBefore(nextRow, row);
            this.reindexFields(container);
        }
    }

    removeRule(container, row) {
        row.remove();
        this.reindexFields(container);
        this.updateEmptyState(container);
    }

    reindexFields(container) {
        const fieldPrefix = container.dataset.fieldNamePrefix;
        const validationPrefix = container.dataset.validationPrefix;
        const rows = container.querySelectorAll('[data-welcome-rule-row]');
        const priorityStart = parseInt(container.dataset.priorityStart) || 1000;
        const priorityMode = container.dataset.priorityMode || 'descending';

        rows.forEach((row, index) => {
            // Update all input/select/textarea name and id attributes
            const fields = row.querySelectorAll('[data-field]');

            fields.forEach(field => {
                const fieldName = field.dataset.field;
                const oldName = field.getAttribute('name');

                if (oldName) {
                    // Replace the index in the name attribute
                    const newName = oldName.replace(/\[\d+\]/g, `[${index}]`);
                    field.setAttribute('name', newName);
                }

                // Update id attributes
                const oldId = field.getAttribute('id');
                if (oldId && oldId.includes('_')) {
                    const idPattern = new RegExp(`${fieldPrefix.replace(/\./g, '_')}_([0-9]+)__`, 'g');
                    const newId = oldId.replace(idPattern, `${fieldPrefix.replace(/\./g, '_')}_${index}__`);
                    field.setAttribute('id', newId);
                }

                // Update for attribute in labels
                const label = row.querySelector(`label[for="${oldId}"]`);
                if (label && newId) {
                    label.setAttribute('for', newId);
                }

                // Update data-valmsg-for attribute for all validation spans
                row.querySelectorAll(`[data-valmsg-for]`).forEach(validationField => {
                    const oldValmsgFor = validationField.getAttribute('data-valmsg-for');
                    if (oldValmsgFor) {
                        const newValmsgFor = oldValmsgFor.replace(/\[\d+\]/g, `[${index}]`);
                        validationField.setAttribute('data-valmsg-for', newValmsgFor);
                    }
                });
            });

            // Update priority field if present
            const priorityField = row.querySelector('[data-field="priority"]');
            if (priorityField && priorityMode !== 'none') {
                const newPriority = priorityMode === 'descending' ? priorityStart - index : priorityStart + index;
                priorityField.value = newPriority;
            }
        });
    }

    updateEmptyState(container) {
        const emptyStateId = container.dataset.emptyStateId;
        const emptyState = document.getElementById(emptyStateId);
        const rows = container.querySelectorAll('[data-welcome-rule-row]');

        if (emptyState) {
            if (rows.length === 0) {
                emptyState.classList.remove('d-none');
            } else {
                emptyState.classList.add('d-none');
            }
        }
    }
}

// Initialize on document ready
document.addEventListener('DOMContentLoaded', () => {
    new WelcomeRulesManager();
});
