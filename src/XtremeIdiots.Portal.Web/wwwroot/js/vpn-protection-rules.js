(function () {
    'use strict';

    var allowedOperators = {
        boolean: ['Equal', 'NotEqual'],
        numeric: ['Equal', 'NotEqual', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual'],
        status: ['Equal', 'NotEqual'],
        string: ['Equal', 'NotEqual', 'Contains']
    };

    var operatorHints = {
        boolean: 'Boolean signals support equal and not equal.',
        numeric: 'Numeric signals support equality and range comparisons.',
        status: 'Status signals support equal and not equal.',
        string: 'Text signals support equal, not equal, and contains.'
    };

    function normalizeId(value) {
        return value.replaceAll('.', '_').replaceAll('[', '_').replaceAll(']', '');
    }

    function syncCheckbox(row) {
        row.querySelectorAll('input[type="checkbox"][data-property]').forEach(function (checkbox) {
            var property = checkbox.dataset.property;
            var hidden = row.querySelector('input[type="hidden"][data-checkbox-hidden="' + property + '"]');
            if (hidden) {
                hidden.disabled = checkbox.checked;
            }
        });
    }

    function getSelectedOption(select) {
        return select && select.selectedIndex >= 0 ? select.options[select.selectedIndex] : null;
    }

    function getSignalMetadata(row) {
        var signalSelect = row.querySelector('[data-role="vpn-signal"]');
        var signalOption = getSelectedOption(signalSelect);
        if (signalOption && signalOption.value) {
            return signalOption.dataset;
        }

        var ruleIdSelect = row.querySelector('[data-role="vpn-global-rule"]');
        var inheritedOption = getSelectedOption(ruleIdSelect);
        return inheritedOption ? inheritedOption.dataset : {};
    }

    function createOption(value, label) {
        var option = document.createElement('option');
        option.value = value;
        option.textContent = label;
        return option;
    }

    function createExpectedValueControl(kind, currentValue, allowInherit, resetValue) {
        var control;
        var normalizedValue = currentValue || '';

        if (kind === 'boolean' || kind === 'status') {
            control = document.createElement('select');
            control.className = 'form-select';
            if (allowInherit) {
                control.appendChild(createOption('', 'Inherit'));
            }

            var values = kind === 'boolean'
                ? [['true', 'True'], ['false', 'False']]
                : [['Success', 'Success'], ['Failed', 'Failed'], ['Unavailable', 'Unavailable']];
            values.forEach(function (item) {
                control.appendChild(createOption(item[0], item[1]));
            });

            var matchingValue = values.find(function (item) {
                return item[0].toLowerCase() === normalizedValue.toLowerCase();
            });
            if (!resetValue && normalizedValue && !matchingValue) {
                control.appendChild(createOption(normalizedValue, normalizedValue + ' (invalid)'));
            }
            control.value = resetValue
                ? (allowInherit ? '' : values[0][0])
                : (matchingValue ? matchingValue[0] : (normalizedValue || (allowInherit ? '' : values[0][0])));
        } else {
            control = document.createElement('input');
            control.className = 'form-control';
            control.type = kind === 'numeric' ? 'number' : 'text';
            control.value = resetValue ? '' : normalizedValue;
            control.placeholder = allowInherit ? 'Inherit' : '';

            if (kind === 'numeric') {
                control.min = '0';
                control.max = '100';
                control.step = '1';
                control.inputMode = 'numeric';
            } else {
                control.maxLength = 256;
            }
        }

        control.dataset.property = 'ExpectedValue';
        control.dataset.role = 'vpn-expected-value';
        control.dataset.valueKind = kind;
        return control;
    }

    function configureOperator(row, kind, allowInherit) {
        var operator = row.querySelector('[data-role="vpn-operator"]');
        if (!operator) return;

        var supported = allowedOperators[kind] || allowedOperators.string;
        Array.from(operator.options).forEach(function (option) {
            var isSupported = option.value === '' ? allowInherit : supported.includes(option.value);
            option.hidden = !isSupported;
            option.disabled = !isSupported;
        });

        if (operator.value && !supported.includes(operator.value)) {
            operator.value = allowInherit ? '' : supported[0];
        }

        var hint = row.querySelector('[data-role="vpn-operator-hint"]');
        if (hint) {
            hint.textContent = operatorHints[kind] || operatorHints.string;
        }
    }

    function configureExpectedValue(row, kind, allowInherit, resetValue) {
        var current = row.querySelector('[data-role="vpn-expected-value"]');
        if (!current) return;

        if (current.dataset.valueKind === kind && !resetValue) {
            return;
        }

        var replacement = createExpectedValueControl(kind, current.value, allowInherit, resetValue);
        Array.from(current.attributes).forEach(function (attribute) {
            if (attribute.name.startsWith('data-val') || attribute.name.startsWith('aria-')) {
                replacement.setAttribute(attribute.name, attribute.value);
            }
        });
        current.replaceWith(replacement);
    }

    function configureRule(root, row, resetValue) {
        var metadata = getSignalMetadata(row);
        var kind = metadata.valueKind || 'string';
        var allowInherit = root.dataset.ruleMode === 'override';

        configureOperator(row, kind, allowInherit);
        configureExpectedValue(row, kind, allowInherit, resetValue);

        var signalHint = row.querySelector('[data-role="vpn-signal-hint"]');
        if (signalHint) {
            signalHint.textContent = metadata.signalDescription || 'Choose the intelligence value this rule should inspect.';
        }

        var expectedHint = row.querySelector('[data-role="vpn-expected-hint"]');
        if (expectedHint) {
            expectedHint.textContent = metadata.valueHint || 'Enter the value to compare.';
        }
    }

    function reindex(root) {
        var prefix = root.dataset.fieldPrefix;
        root.querySelectorAll('[data-vpn-rule-row]').forEach(function (row, index) {
            row.dataset.index = index.toString();
            row.querySelectorAll('[data-property]').forEach(function (control) {
                var property = control.dataset.property;
                var fieldName = prefix + '[' + index + '].' + property;
                control.name = fieldName;
                if (control.type !== 'hidden' || control.dataset.checkboxHidden === undefined) {
                    control.id = normalizeId(fieldName);
                }
            });
            row.querySelectorAll('[data-label-for]').forEach(function (label) {
                label.htmlFor = normalizeId(prefix + '[' + index + '].' + label.dataset.labelFor);
            });
            row.querySelectorAll('[data-validation-for]').forEach(function (validation) {
                validation.setAttribute('data-valmsg-for', prefix + '[' + index + '].' + validation.dataset.validationFor);
            });
            syncCheckbox(row);
        });
    }

    function wireRow(root, row) {
        var removeButton = row.querySelector('[data-action="remove-vpn-rule"]');
        if (removeButton) {
            removeButton.addEventListener('click', function () {
                row.remove();
                reindex(root);
            });
        }

        row.querySelectorAll('input[type="checkbox"][data-property]').forEach(function (checkbox) {
            checkbox.addEventListener('change', function () {
                syncCheckbox(row);
            });
        });

        var signal = row.querySelector('[data-role="vpn-signal"]');
        if (signal) {
            signal.addEventListener('change', function () {
                configureRule(root, row, true);
                reindex(root);
            });
        }

        var globalRule = row.querySelector('[data-role="vpn-global-rule"]');
        if (globalRule) {
            globalRule.addEventListener('change', function () {
                configureRule(root, row, false);
                reindex(root);
            });
        }

        configureRule(root, row, false);
        syncCheckbox(row);
    }

    function initialize(root) {
        var template = document.getElementById(root.dataset.templateId);
        var addButton = document.getElementById(root.dataset.addButtonId);
        if (!template || !addButton) return;

        root.querySelectorAll('[data-vpn-rule-row]').forEach(function (row) {
            wireRow(root, row);
        });

        addButton.addEventListener('click', function () {
            var row = template.content.firstElementChild.cloneNode(true);
            root.appendChild(row);
            wireRow(root, row);
            reindex(root);
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                window.jQuery.validator.unobtrusive.parse(row);
            }
            row.querySelector('input, select')?.focus();
        });

        reindex(root);
    }

    document.querySelectorAll('[data-vpn-rules-container]').forEach(initialize);

    document.querySelectorAll('form[method="post"]').forEach(function (form) {
        form.addEventListener('submit', function () {
            document.querySelectorAll('[data-vpn-rules-container]').forEach(reindex);
        });
    });
}());
