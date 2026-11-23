(function () {
    'use strict';

    var supportedOperators = {
        boolean: ['Equal', 'NotEqual'],
        numeric: ['Equal', 'NotEqual', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual'],
        status: ['Equal', 'NotEqual'],
        string: ['Equal', 'NotEqual', 'Contains']
    };

    var operatorHints = {
        boolean: 'Boolean signals support equal and not equal.',
        numeric: 'Numeric signals support equality and range comparisons.',
        status: 'Status signals support equal and not equal.',
        string: 'String signals support equality, inequality, and contains.'
    };

    function normalizeId(value) {
        return value.replaceAll('.', '_').replaceAll('[', '_').replaceAll(']', '');
    }

    function setDisabled(container, disabled) {
        container.querySelectorAll('input, select, textarea, button').forEach(function (control) {
            control.disabled = disabled;
        });
    }

    function configureOperator(node, kind) {
        var operator = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="Operator"]');
        var hint = node.querySelector(':scope > [data-condition-leaf] [data-vpn-operator-hint]');
        if (!operator) return;

        var supported = supportedOperators[kind] || supportedOperators.string;
        Array.from(operator.options).forEach(function (option) {
            option.hidden = option.value !== '' && !supported.includes(option.value);
        });
        if (operator.value && !supported.includes(operator.value)) {
            operator.value = 'Equal';
        }
        if (hint) {
            hint.textContent = operatorHints[kind] || operatorHints.string;
        }
    }

    function updateNode(node) {
        var kindSelector = node.querySelector(':scope > .row [data-condition-kind-selector]');
        if (kindSelector) kindSelector.disabled = false;
        var kind = kindSelector?.value || node.dataset.conditionKind || 'group';
        node.dataset.conditionKind = kind;

        node.querySelectorAll(':scope > .row button').forEach(function (button) {
            button.disabled = false;
        });
        var group = node.querySelector(':scope > [data-condition-group]');
        var leaf = node.querySelector(':scope > [data-condition-leaf]');
        var children = node.querySelector(':scope > [data-condition-group] [data-siege-children]');
        var playerSignal = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="PlayerSignal"]');
        var vpnSignal = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="Signal"]');

        if (group) {
            group.hidden = kind !== 'group';
            var groupOperator = group.querySelector('[data-condition-field="GroupOperator"]');
            if (groupOperator) groupOperator.disabled = kind !== 'group';
        }
        if (leaf) {
            leaf.hidden = kind === 'group';
            setDisabled(leaf, kind === 'group');
        }

        var playerContainer = node.querySelector(':scope > [data-condition-leaf] [data-player-signal-container]');
        var vpnContainer = node.querySelector(':scope > [data-condition-leaf] [data-vpn-signal-container]');
        if (playerContainer) {
            playerContainer.hidden = kind !== 'player';
            setDisabled(playerContainer, kind !== 'player');
        }
        if (vpnContainer) {
            vpnContainer.hidden = kind !== 'vpn';
            setDisabled(vpnContainer, kind !== 'vpn');
        }

        var addButtons = node.querySelectorAll(':scope > .row [data-action^="add-siege-"]');
        addButtons.forEach(function (button) {
            button.hidden = kind !== 'group';
        });

        if (kind === 'player') {
            configureOperator(node, 'boolean');
            var playerHint = node.querySelector(':scope > [data-condition-leaf] [data-player-signal-hint]');
            if (playerHint) {
                playerHint.textContent = playerSignal?.value === 'PlayerTag'
                    ? 'Compare the player tag name. Available tags are suggested as you type.'
                    : 'New GUID and new IP refer to the player history.';
            }
        } else if (kind === 'vpn') {
            var signalOption = vpnSignal?.selectedOptions[0];
            var valueKind = signalOption?.dataset.valueKind || 'string';
            configureOperator(node, valueKind);

            var signalHint = node.querySelector(':scope > [data-condition-leaf] [data-vpn-signal-hint]');
            var expectedHint = node.querySelector(':scope > [data-condition-leaf] [data-vpn-expected-hint]');
            if (signalHint) {
                signalHint.textContent = signalOption?.dataset.signalDescription || 'Select the VPN Protection value to inspect.';
            }
            if (expectedHint) {
                expectedHint.textContent = signalOption?.dataset.valueHint || 'Choose the value this condition should match.';
            }
        }

        if (children) {
            children.hidden = kind !== 'group';
            if (kind === 'group') {
                children.querySelectorAll(':scope > [data-siege-condition-node]').forEach(updateNode);
            } else {
                setDisabled(children, true);
            }
        }
    }

    function setNodeKind(node, kind) {
        var selector = node.querySelector('[data-condition-kind-selector]');
        if (selector) selector.value = kind;
        node.dataset.conditionKind = kind;
        var groupOperator = node.querySelector(':scope > [data-condition-group] > .row [data-condition-field="GroupOperator"]');
        var playerSignal = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="PlayerSignal"]');
        var vpnSignal = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="Signal"]');
        var operator = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="Operator"]');
        var expectedValue = node.querySelector(':scope > [data-condition-leaf] [data-condition-field="ExpectedValue"]');

        if (kind === 'group') {
            if (groupOperator) groupOperator.value = 'And';
        } else {
            if (operator) operator.value = 'Equal';
            if (kind === 'player') {
                if (playerSignal) playerSignal.value = 'NewGuid';
                if (expectedValue) expectedValue.value = 'true';
            } else {
                if (vpnSignal) vpnSignal.value = 'ProxyCheckIsVpn';
                if (expectedValue) expectedValue.value = 'true';
            }
        }
        updateNode(node);
    }

    function reindexNode(node, prefix) {
        node.dataset.conditionPrefix = prefix;
        var kindSelector = node.querySelector(':scope > .row [data-condition-kind-selector]');
        if (kindSelector) {
            kindSelector.id = normalizeId(prefix + '.Kind');
            var kindLabel = node.querySelector('[data-condition-label="Kind"]');
            if (kindLabel) kindLabel.htmlFor = kindSelector.id;
        }
        node.querySelectorAll(':scope > [data-condition-group] > .row [data-condition-field], :scope > [data-condition-leaf] [data-condition-field]')
            .forEach(function (control) {
                var field = control.dataset.conditionField;
                var name = prefix + '.' + field;
                control.name = name;
                control.id = normalizeId(name);
                var label = node.querySelector(
                    ':scope > [data-condition-group] > .row [data-condition-label="' + field + '"], ' +
                    ':scope > [data-condition-leaf] [data-condition-label="' + field + '"]');
                if (label) label.htmlFor = control.id;
            });

        var children = node.querySelector(':scope > [data-condition-group] [data-siege-children]');
        if (children) {
            Array.from(children.querySelectorAll(':scope > [data-siege-condition-node]'))
                .forEach(function (child, index) {
                    reindexNode(child, prefix + '.Conditions[' + index + ']');
                });
        }
        updateNode(node);
    }

    function initialize(root) {
        var ruleTemplate = document.getElementById('global-siege-rule-template');
        var conditionTemplate = document.getElementById('siege-mode-condition-template');
        var addRule = document.getElementById('add-global-siege-rule');
        if (!ruleTemplate || !conditionTemplate || !addRule) return;

        function reindexRules() {
            var rows = Array.from(root.querySelectorAll(':scope > [data-siege-rule-row]'));
            var count = document.getElementById('SiegeMode_SubmittedRuleCount');
            if (count) count.value = rows.length.toString();
            rows.forEach(function (row, index) {
                var rulePrefix = 'SiegeMode.Rules[' + index + ']';
                row.querySelectorAll('[data-rule-field]').forEach(function (control) {
                    var field = control.dataset.ruleField;
                    if (field === 'Enabled-hidden') return;
                    var name = rulePrefix + '.' + field;
                    control.name = name;
                    control.id = normalizeId(name);
                });
                var hiddenEnabled = row.querySelector('[data-rule-field="Enabled-hidden"]');
                if (hiddenEnabled) hiddenEnabled.name = rulePrefix + '.Enabled';
                row.querySelectorAll('[data-rule-label]').forEach(function (label) {
                    label.htmlFor = normalizeId(rulePrefix + '.' + label.dataset.ruleLabel);
                });
                var condition = row.querySelector(':scope > [data-siege-condition-node], :scope > [data-siege-condition-mount] > [data-siege-condition-node]');
                if (!condition) {
                    var mount = row.querySelector('[data-siege-condition-mount]');
                    if (mount) {
                        condition = conditionTemplate.content.firstElementChild.cloneNode(true);
                        condition = replacePrefix(condition, '__prefix__', rulePrefix + '.Condition');
                        mount.appendChild(condition);
                        setNodeKind(condition, 'group');
                    }
                }
                if (condition) reindexNode(condition, rulePrefix + '.Condition');
            });
        }

        function replacePrefix(node, placeholder, prefix) {
            node.querySelectorAll('*').forEach(function (element) {
                Array.from(element.attributes).forEach(function (attribute) {
                    if (attribute.value.includes(placeholder)) {
                        element.setAttribute(attribute.name, attribute.value.replaceAll(placeholder, prefix));
                    }
                });
            });
            return node;
        }

        function addCondition(parentNode, kind, playerSignalValue) {
            var group = parentNode.querySelector(':scope > [data-condition-group]');
            var children = group?.querySelector(':scope > [data-siege-children]');
            if (!children) return;

            var childIndex = children.querySelectorAll(':scope > [data-siege-condition-node]').length;
            var childPrefix = parentNode.dataset.conditionPrefix + '.Conditions[' + childIndex + ']';
            var child = conditionTemplate.content.firstElementChild.cloneNode(true);
            child = replacePrefix(child, '__prefix__', childPrefix);
            children.appendChild(child);

            if (playerSignalValue) {
                var signal = child.querySelector('[data-condition-field="PlayerSignal"]');
                if (signal) signal.value = playerSignalValue;
            }
            setNodeKind(child, kind);
            reindexNode(child, childPrefix);
            return child;
        }

        root.addEventListener('click', function (event) {
            var button = event.target.closest('[data-action]');
            if (!button) return;

            if (button.id === 'add-global-siege-rule') {
                return;
            }

            if (button.dataset.action === 'remove-siege-rule') {
                button.closest('[data-siege-rule-row]')?.remove();
                reindexRules();
            } else if (button.dataset.action === 'remove-siege-condition') {
                var condition = button.closest('[data-siege-condition-node]');
                var parentGroup = condition?.parentElement.closest('[data-siege-condition-node]');
                if (condition && parentGroup) {
                    condition.remove();
                    reindexRules();
                }
            } else {
                var parentNode = button.closest('[data-siege-condition-node]');
                if (!parentNode) return;
                if (button.dataset.action === 'add-siege-group') {
                    var groupNode = addCondition(parentNode, 'group');
                    if (groupNode) addCondition(groupNode, 'player');
                } else if (button.dataset.action === 'add-siege-player-condition') {
                    addCondition(parentNode, 'player');
                } else if (button.dataset.action === 'add-siege-vpn-condition') {
                    addCondition(parentNode, 'vpn');
                }
                reindexRules();
            }
        });

        root.addEventListener('change', function (event) {
            var node = event.target.closest('[data-siege-condition-node]');
            if (!node) return;
            if (event.target.matches('[data-condition-kind-selector]')) {
                setNodeKind(node, event.target.value);
            } else if (event.target.matches('[data-condition-field="Signal"], [data-condition-field="PlayerSignal"]')) {
                updateNode(node);
            }
        });

        addRule.addEventListener('click', function () {
            var row = ruleTemplate.content.firstElementChild.cloneNode(true);
            root.appendChild(row);
            reindexRules();
            var condition = row.querySelector('[data-siege-condition-node]');
            if (condition) addCondition(condition, 'player', 'NewGuid');
            reindexRules();
            var index = root.querySelectorAll(':scope > [data-siege-rule-row]').length - 1;
            var id = row.querySelector('[data-rule-field="Id"]');
            if (id) id.value = 'new-rule-' + (index + 1);
        });

        root.querySelectorAll('[data-siege-condition-node]').forEach(function (node) {
            updateNode(node);
        });
        reindexRules();
        root.closest('form')?.addEventListener('submit', reindexRules);
    }

    document.querySelectorAll('#global-siege-rules').forEach(initialize);
}());
