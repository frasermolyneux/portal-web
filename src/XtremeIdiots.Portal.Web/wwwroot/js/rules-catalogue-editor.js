(() => {
    "use strict";
    if (window.rulesCatalogueEditorLoaded) return;
    window.rulesCatalogueEditorLoaded = true;

    const element = (tag, text, className) => {
        const node = document.createElement(tag);
        if (text !== undefined) node.textContent = text;
        if (className) node.className = className;
        return node;
    };
    const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
    const labelText = value => value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, c => c.toUpperCase());

    document.querySelectorAll("[data-rules-catalogue-editor]").forEach(root => {
        const hidden = root.querySelector("[data-rules-value]");
        const host = root.querySelector("[data-rules-editor]");
        const form = root.closest("form");
        let catalogue, tags, compatibility, globals, rules, gameTypes, tokens, effectiveTimings;
        let error = "";
        try {
            catalogue = JSON.parse(root.dataset.catalogue);
            tags = JSON.parse(root.dataset.tags);
            compatibility = JSON.parse(root.dataset.compatibility);
            gameTypes = JSON.parse(root.dataset.gameTypes || "{}");
            tokens = JSON.parse(root.dataset.tokens || "[]");
            effectiveTimings = JSON.parse(root.dataset.effectiveTimings || "null");
            globals = JSON.parse(root.dataset.globalRules);
            rules = JSON.parse(hidden.value);
            if (!Array.isArray(rules)) throw new Error("Rules must be a JSON array.");
            if (!Array.isArray(globals)) throw new Error("Global rules must be a JSON array.");
        } catch (e) {
            error = `Unable to load rules: ${e.message}`;
        }
        const override = root.dataset.override === "true";
        const server = root.dataset.serverContext === "true";
        const sync = () => { hidden.value = JSON.stringify(rules); };
        form?.addEventListener("submit", event => {
            const invalid = host.querySelector("[data-rule-timing]:invalid");
            if (invalid) {
                event.preventDefault();
                event.stopImmediatePropagation();
                invalid.reportValidity();
            }
        }, true);
        const button = (text, fn, danger = false, disabled = false) => {
            const b = element("button", text, `btn btn-sm ${danger ? "btn-outline-danger" : "btn-outline-secondary"} me-1 mb-2`);
            b.type = "button";
            b.disabled = disabled;
            b.addEventListener("click", fn);
            return b;
        };
        const field = (parent, title, control, help) => {
            const wrapper = element("div", undefined, "mb-3");
            const label = element("label", title, "form-label d-block");
            label.append(control);
            wrapper.append(label);
            if (help) wrapper.append(element("div", help, "form-text"));
            parent.append(wrapper);
            return control;
        };
        const input = (parent, title, value, change, type = "text", required = false) => {
            const node = element(type === "textarea" ? "textarea" : "input", undefined, "form-control");
            if (type !== "textarea") node.type = type;
            node.value = value ?? "";
            node.required = required;
            node.addEventListener("input", () => { change(node.value, node); sync(); });
            return field(parent, title, node);
        };
        const select = (parent, title, value, choices, change, multiple = false) => {
            const node = element("select", undefined, "form-select");
            node.multiple = multiple;
            const selected = multiple ? (Array.isArray(value) ? value : []) : [value];
            const entries = choices.map(item => typeof item === "string" ? { value: item, text: labelText(item) } : item);
            if (!multiple && (value == null || value === "")) entries.unshift({ value: "", text: "Select an option" });
            selected.filter(item => item != null && !entries.some(choice => choice.value === item))
                .forEach(item => entries.unshift({ value: item, text: `${item} (unsupported or unavailable)` }));
            entries.forEach(choice => {
                const option = element("option", choice.text);
                option.value = choice.value;
                option.selected = selected.includes(choice.value);
                node.append(option);
            });
            if (!multiple) { node.required = true; if (value == null || value === "") node.value = ""; }
            node.addEventListener("change", () => {
                change(multiple ? Array.from(node.selectedOptions, option => option.value) : node.value);
                sync();
            });
            return field(parent, title, node);
        };
        const check = (parent, title, value, change, disabled = false) => {
            const wrap = element("label", undefined, "form-check mb-2");
            const box = element("input", undefined, "form-check-input");
            box.type = "checkbox"; box.checked = value === true; box.disabled = disabled;
            box.addEventListener("change", () => { change(box.checked); sync(); });
            wrap.append(box, element("span", title, "form-check-label"));
            parent.append(wrap);
        };
        const details = (parent, title, value) => {
            const panel = element("details", undefined, "mb-2");
            panel.append(element("summary", title), element("pre", JSON.stringify(value, null, 2) ?? "undefined"));
            parent.append(panel);
        };
        const extensions = (parent, value, keys) => {
            const extra = Object.fromEntries(Object.entries(value).filter(([key]) => !keys.includes(key)));
            if (Object.keys(extra).length) details(parent, "Additional preserved properties", extra);
        };
        const info = (parent, id, context = "Compatibility") => {
            const status = compatibility[id];
            const text = status === true ? "supported for this server." :
                status === false ? "unsupported for this server." : "unknown; server capability evidence is unavailable.";
            const effect = catalogue?.conditions?.some(item => item.conditionId === id) || context.toLowerCase().includes("condition") ?
                " An unsupported condition evaluates as Unknown and is not removed." :
                " An unsupported action is skipped.";
            parent.append(element("p", `${context}: ${text}${status === true ? "" : effect}`,
                status === true ? "form-text" : "alert alert-warning py-1"));
        };
        const feature = catalogue?.features?.find(item => item.featureId === root.dataset.featureId);
        const conditions = catalogue?.conditions?.filter(item => feature?.conditionIds?.includes(item.conditionId)) || [];
        const actions = catalogue?.actions?.filter(item => feature?.actionTypeIds?.includes(item.actionTypeId)) || [];
        const availableConditions = conditions.filter(item => !server || compatibility[item.conditionId] === true);
        const availableActions = actions.filter(item => !server || compatibility[item.actionTypeId] === true);
        const conditionChoices = availableConditions.map(item => ({ value: item.conditionId, text: item.displayName }));
        const actionChoices = availableActions.map(item => ({ value: item.actionTypeId, text: item.displayName }));
        const gameSupport = (parent, definition) => {
            if (server || !definition) return;
            const row = element("div", undefined, "form-text mb-2");
            row.append(element("strong", "Supported games: "));
            if (!Array.isArray(definition.gameTypes) || !definition.gameTypes.length) {
                row.append(document.createTextNode("Unknown"));
            } else {
                definition.gameTypes.forEach(game => {
                    const gameType = gameTypes[game];
                    const name = typeof gameType === "string" ? gameType : gameType?.name;
                    const displayName = typeof gameType === "string" ? gameType : gameType?.label;
                    const label = element("span", undefined, "me-2");
                    if (name && root.dataset.gameIconBase) {
                        const image = element("img");
                        image.src = root.dataset.gameIconBase + encodeURIComponent(name) + ".png";
                        image.width = 16; image.height = 16; image.alt = "";
                        image.title = displayName || name;
                        label.append(image, document.createTextNode(` ${displayName || name}`));
                    } else label.textContent = `Game ${game} (name unavailable)`;
                    row.append(label);
                });
            }
            parent.append(row);
        };
        const firstSeen = id => id === "player.first-seen-guid" || id === "player.first-seen-ip";
        const vpnKind = signal => {
            if (["ProxyCheckRiskScore", "MaxMindAnonymizerConfidence"].includes(signal)) return "number";
            if (["MaxMindStatus", "ProxyCheckStatus"].includes(signal)) return "status";
            if (["ProxyCheckProxyType", "ProxyCheckAsNumber", "ProxyCheckAsOrganization", "MaxMindProviderName"].includes(signal)) return "string";
            return "boolean";
        };
        const vpnOperators = kind => kind === "number" ?
            ["Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual"] :
            kind === "string" ? ["Equal", "NotEqual", "Contains"] : ["Equal", "NotEqual"];
        const tokenPicker = (parent, control, item, name, parameterHost = parent) => {
            if (!Array.isArray(tokens) || !tokens.length ||
                (item.templateParameters != null && (!Array.isArray(item.templateParameters) || item.templateParameters.some(value => typeof value !== "string")))) return;
            const picker = element("details", undefined, "mb-3");
            picker.append(element("summary", "Insert a token"));
            const search = element("input", undefined, "form-control my-2");
            search.type = "search";
            search.placeholder = "Search tokens";
            search.setAttribute("aria-label", "Search tokens");
            picker.append(search);
            const groups = new Map();
            tokens.filter(token => token && typeof token.token === "string").forEach(token => {
                const group = token.group || "Other";
                if (!groups.has(group)) {
                    const section = element("section", undefined, "mb-2");
                    section.append(element("h6", group));
                    groups.set(group, section);
                    picker.append(section);
                }
                const row = element("div", undefined, "mb-2");
                const text = [token.label, token.token, token.description, token.availability, token.sampleValue, token.fallback,
                    ...(Array.isArray(token.requiredSources) ? token.requiredSources : [])]
                    .filter(value => value != null).join(" ");
                row.dataset.search = text.toLowerCase();
                row.append(button(token.label || token.token, () => {
                    const start = control.selectionStart ?? control.value.length;
                    const end = control.selectionEnd ?? start;
                    control.setRangeText(token.token, start, end, "end");
                    control.dispatchEvent(new Event("input", { bubbles: true }));
                    const checkbox = [...parameterHost.querySelectorAll("[data-template-parameter]")]
                        .find(input => input.dataset.templateParameter === name);
                    if (checkbox && !checkbox.checked) {
                        checkbox.checked = true;
                        checkbox.dispatchEvent(new Event("change", { bubbles: true }));
                    }
                    control.focus();
                }));
                row.append(element("span", `${token.token}${token.isSensitive ? " - sensitive" : ""}`,
                    token.isSensitive ? "form-text text-danger" : "form-text"));
                const description = [
                    token.description,
                    token.sampleValue != null ? `Sample: ${token.sampleValue}` : null,
                    token.fallback != null ? `Fallback: ${token.fallback}` : null,
                    token.requiredSources?.length ? `Source: ${token.requiredSources.join(", ")}` : null,
                    token.availability ? `Availability: ${token.availability}` : null
                ].filter(Boolean).join(" ");
                if (description) row.append(element("div", description, "form-text"));
                groups.get(group).append(row);
            });
            search.addEventListener("input", () => {
                const query = search.value.toLowerCase().trim();
                groups.forEach(section => {
                    const rows = [...section.querySelectorAll("[data-search]")];
                    rows.forEach(row => { row.hidden = !row.dataset.search.includes(query); });
                    section.hidden = rows.every(row => row.hidden);
                });
            });
            parent.append(picker);
        };
        const renderParameters = (parent, item, definition, isCondition) => {
            if (!object(item.parameters)) {
                parent.append(element("p", "Malformed parameters are preserved. Replace this entry to edit it.", "alert alert-warning"));
                details(parent, "Stored parameters", item.parameters);
                return;
            }
            const p = item.parameters;
            const isVpn = item.conditionId === "vpn.intelligence";
            if (firstSeen(item.conditionId)) {
                const scope = item.conditionId === "player.first-seen-guid" ? "GUID history is per game." : "IP history spans all games.";
                const reference = root.dataset.featureId === "siege-mode" ?
                    "Choose observed history before this observation or the Siege activation baseline." :
                    "Observed history is measured before the current observation.";
                parent.append(element("p", `${scope} ${reference}`, "form-text"));
            }
            definition.parameters.forEach(schema => {
                const name = schema.name;
                if (name === "postToForums" && (!definition.producesModerationRecord || !definition.supportsForumPublication)) return;
                if (firstSeen(item.conditionId) && name === "value" && ["IsNew", "IsKnown"].includes(p.operator)) return;
                const title = name === "postToForums" ? "Post to forums" : labelText(name);
                const set = value => { p[name] = value; };
                if (isVpn && (name === "operator" || name === "expectedValue") && !p.signal) {
                    parent.append(element("p", `${title}: choose a signal first.`, "form-text"));
                    return;
                }
                if ((name === "tags" && isCondition) || name === "recipientTags") {
                    select(parent, isCondition ? "Player tags" : "Recipient tags", p[name], tags.map(t => ({ value: t.value, text: t.text })), set, true).required = schema.required;
                } else if (schema.type === "boolean") {
                    check(parent, title, p[name], set);
                } else if (isVpn && name === "expectedValue" && vpnKind(p.signal) === "boolean") {
                    select(parent, title, p[name], ["true", "false"], set);
                } else if (isVpn && name === "expectedValue" && vpnKind(p.signal) === "status") {
                    select(parent, title, p[name], ["Success", "Failed", "Unavailable"], set);
                } else if (!isCondition && name === "messages" && schema.supportsMessageList) {
                    if (p[name] != null && (!Array.isArray(p[name]) || p[name].some(value => typeof value !== "string"))) {
                        parent.append(element("p", "Malformed messages are preserved. Replace this action to edit them.", "alert alert-warning"));
                        details(parent, "Stored messages", p[name]);
                    } else {
                        const messages = p[name] ?? [];
                        const section = element("div", undefined, "mb-3");
                        section.append(element("div", title, "form-label"));
                        messages.forEach((message, index) => {
                            const row = element("div", undefined, "border rounded p-2 mb-2");
                            const control = input(row, `Message ${index + 1}`, message, value => { messages[index] = value; }, "textarea", true);
                            if (schema.supportsTemplates) tokenPicker(row, control, item, name, parent);
                            row.append(button("Move message up", () => move(messages, index, -1), false, index === 0));
                            row.append(button("Move message down", () => move(messages, index, 1), false, index === messages.length - 1));
                            row.append(button("Remove message", () => { messages.splice(index, 1); sync(); render(); }, true));
                            section.append(row);
                        });
                        section.append(button("Add message", () => { p[name] = messages; messages.push(""); sync(); render(); }, false, messages.length >= 100));
                        parent.append(section);
                    }
                } else if (schema.allowedValues.length) {
                    let choices = schema.allowedValues;
                    if (isVpn && name === "operator") choices = vpnOperators(vpnKind(p.signal));
                    if (firstSeen(item.conditionId) && name === "referenceScope" && root.dataset.featureId !== "siege-mode")
                        choices = choices.filter(value => value !== "SiegeBaseline");
                    select(parent, title, p[name], choices, value => {
                        set(value);
                        if (isVpn && name === "signal") {
                            delete p.operator;
                            delete p.expectedValue;
                        }
                        if (firstSeen(item.conditionId) && name === "operator") delete p.value;
                        if ((isVpn && name === "signal") || (firstSeen(item.conditionId) && name === "operator")) render();
                    });
                } else {
                    const number = schema.type === "integer" || schema.type === "number";
                    const control = input(parent, title, p[name], (value, node) => {
                        if (number) {
                            if (value !== "" && node.validity.valid) set(Number(value));
                            else delete p[name];
                        } else if (name === "message" && schema.supportsMessageList && value === "") {
                            delete p[name];
                            if (Array.isArray(item.templateParameters)) {
                                item.templateParameters = item.templateParameters.filter(parameter => parameter !== name);
                                const checkbox = [...parent.querySelectorAll("[data-template-parameter]")]
                                    .find(node => node.dataset.templateParameter === name);
                                if (checkbox) checkbox.checked = false;
                            }
                        } else set(value);
                    }, number ? "number" : schema.supportsTemplates ? "textarea" : "text", schema.required || (firstSeen(item.conditionId) && name === "value"));
                    if (schema.maxLength != null && !schema.supportsTemplates && !schema.supportsMessageList) control.maxLength = schema.maxLength;
                    if (schema.minimum != null) control.min = schema.minimum;
                    if (schema.maximum != null) control.max = schema.maximum;
                    if (isVpn && name === "expectedValue" && vpnKind(p.signal) === "number") {
                        control.pattern = "(100|[1-9]?[0-9])";
                        control.title = "Enter a whole number from 0 to 100.";
                    }
                    if (schema.type === "duration" || (firstSeen(item.conditionId) && name === "value")) {
                        const duration = schema.type === "duration" || p.operator === "WithinAge";
                        const validate = () => {
                            const value = control.value;
                            const valid = !value || (duration ?
                                /^(?:\d+|(?:\d+\.)?\d{1,2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?)$/.test(value) :
                                /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) && !Number.isNaN(Date.parse(value)));
                            control.setCustomValidity(valid ? "" : duration ?
                                "Enter a duration such as 7.00:00:00." : "Enter an ISO date and time with a timezone.");
                        };
                        control.addEventListener("input", validate);
                        validate();
                    }
                    if (schema.description) parent.append(element("p", schema.description, "form-text"));
                    if (!isCondition && schema.supportsTemplates) tokenPicker(parent, control, item, name);
                    if (schema.type === "duration" || (firstSeen(item.conditionId) && name === "value"))
                        parent.append(element("p", firstSeen(item.conditionId) && p.operator !== "WithinAge" ? "Enter an ISO date and time, including timezone." : "Enter a duration, for example 7.00:00:00 for seven days.", "form-text"));
                }
                if (!isCondition && schema.supportsTemplates) {
                    if (item.templateParameters != null && (!Array.isArray(item.templateParameters) || item.templateParameters.some(value => typeof value !== "string"))) {
                        parent.append(element("p", "Malformed template settings are preserved. Replace this action to edit them.", "alert alert-warning"));
                    } else {
                        check(parent, `Use tokens in ${title.toLowerCase()}`, item.templateParameters?.includes(name), enabled => {
                            const templates = item.templateParameters ?? [];
                            if (enabled && !Object.hasOwn(p, name)) p[name] = name === "messages" ? [] : "";
                            if (!enabled && name === "message" && schema.supportsMessageList && p[name] === "") delete p[name];
                            item.templateParameters = enabled ? [...new Set([...templates, name])] : templates.filter(value => value !== name);
                        });
                        parent.lastElementChild.querySelector("input").dataset.templateParameter = name;
                    }
                }
            });
            const known = new Set(definition.parameters.map(parameter => parameter.name));
            const extra = Object.fromEntries(Object.entries(p).filter(([key]) => !known.has(key)));
            if (Object.keys(extra).length) details(parent, "Additional preserved parameters", extra);
        };
        const move = (list, index, direction) => {
            const target = index + direction;
            if (target < 0 || target >= list.length) return;
            [list[index], list[target]] = [list[target], list[index]];
            sync(); render();
        };
        const conditionStats = node => {
            if (!object(node) || !Array.isArray(node.conditions)) return { count: 1, height: 1 };
            const children = node.conditions.map(conditionStats);
            return { count: 1 + children.reduce((sum, item) => sum + item.count, 0), height: 1 + Math.max(0, ...children.map(item => item.height)) };
        };
        const renderCondition = (parent, node, replace, depth = 1, rootNode = node) => {
            const panel = element("div", undefined, "border rounded p-3 mb-2");
            parent.append(panel);
            if (!object(node)) {
                panel.append(element("p", "Unsupported or missing condition (preserved).", "alert alert-warning"));
                details(panel, "Stored condition", node);
                panel.append(button("Replace with condition", () => { replace(newLeaf()); sync(); render(); }, false, !conditionChoices.length));
                return;
            }
            if (node.groupOperator != null && Array.isArray(node.conditions)) {
                const isAny = node.groupOperator === "Or";
                const isAll = node.groupOperator === "And";
                const color = isAny ? "info" : isAll ? "primary" : "warning";
                const header = element("div", undefined, `rounded p-2 mb-3 bg-${color} bg-opacity-10`);
                const badge = element("span", undefined, `badge bg-${color} me-2`);
                const icon = element("i", undefined, `fa-solid fa-fw ${isAny ? "fa-code-branch" : isAll ? "fa-layer-group" : "fa-triangle-exclamation"}`);
                icon.setAttribute("aria-hidden", "true");
                badge.append(icon, document.createTextNode(isAny ? " ANY" : isAll ? " ALL" : " UNKNOWN"));
                header.append(badge, document.createTextNode(isAny ? "Any condition can match" :
                    isAll ? "All conditions must match" : `Unsupported group operator: ${node.groupOperator}`));
                panel.append(header);
                select(panel, "Match", node.groupOperator, [{ value: "And", text: "All (AND)" }, { value: "Or", text: "Any (OR)" }], value => { node.groupOperator = value; render(); });
                node.conditions.forEach((child, index) => {
                    const row = element("div", undefined, "ms-3 ps-3 border-start");
                    renderCondition(row, child, value => { node.conditions[index] = value; }, depth + 1, rootNode);
                    row.append(button("Remove condition", () => { node.conditions.splice(index, 1); sync(); render(); }, true));
                    panel.append(row);
                });
                if (depth < 8 && conditionStats(rootNode).count < 100) {
                    panel.append(button("Add condition", () => { node.conditions.push(newLeaf()); sync(); render(); }, false, !conditionChoices.length));
                    if (depth < 7 && conditionStats(rootNode).count < 99) panel.append(button("Add group", () => { node.conditions.push({ groupOperator: "And", conditions: [newLeaf()] }); sync(); render(); }, false, !conditionChoices.length));
                }
            } else if (typeof node.conditionId === "string" && node.groupOperator == null && node.conditions == null) {
                select(panel, "Condition", node.conditionId, conditionChoices, id => {
                    const previous = conditions.find(item => item.conditionId === node.conditionId);
                    const next = availableConditions.find(item => item.conditionId === id);
                    if (!next) return;
                    const extras = object(node.parameters) ? { ...node.parameters } : {};
                    previous?.parameters.forEach(param => delete extras[param.name]);
                    next.parameters.forEach(param => delete extras[param.name]);
                    node.conditionId = id; node.parameters = extras;
                    render();
                });
                const definition = conditions.find(item => item.conditionId === node.conditionId);
                gameSupport(panel, definition);
                if (server && node.conditionId) info(panel, node.conditionId, "Condition compatibility");
                if (definition) renderParameters(panel, node, definition, true);
                else if (node.conditionId) details(panel, "Unsupported condition (preserved)", node);
            } else {
                panel.append(element("p", "Unsupported condition shape (preserved).", "alert alert-warning"));
                details(panel, "Stored condition", node);
            }
            extensions(panel, node, ["groupOperator", "conditions", "conditionId", "parameters"]);
            if (depth + conditionStats(node).height <= 8 && conditionStats(rootNode).count < 100) panel.append(button("Wrap in AND/OR group", () => {
                replace({ groupOperator: "And", conditions: [node] }); sync(); render();
            }));
        };
        const newLeaf = () => {
            return { conditionId: "", parameters: {} };
        };
        const newAction = () => {
            return { id: crypto.randomUUID(), typeId: "", parameters: {}, dependsOnActionIds: [] };
        };
        const inheritedCompatibility = (parent, inherited, rule) => {
            const ids = [];
            const walk = condition => {
                if (!object(condition)) return;
                if (typeof condition.conditionId === "string" && condition.conditionId)
                    ids.push({ id: condition.conditionId, kind: "condition" });
                if (Array.isArray(condition.conditions)) condition.conditions.forEach(walk);
            };
            if (rule.condition == null) walk(inherited.condition);
            if (rule.actions == null && Array.isArray(inherited.actions))
                inherited.actions.filter(object).forEach(action => {
                    if (typeof action.typeId === "string" && action.typeId) ids.push({ id: action.typeId, kind: "action" });
                });
            ids.forEach(({ id, kind }) => info(parent, id, `Inherited ${kind} ${id}`));
        };
        const actionPreviews = (panel, action, definition) => {
            if (!root.dataset.previewUrl || !object(action.parameters)) return;
            const names = [...new Set(definition.parameters
                .filter(schema => schema.supportsTemplates)
                .map(schema => schema.supportsMessageList ? "message" : schema.name))];
            names.forEach(name => {
                const container = element("section", undefined, "border rounded p-3 mb-3");
                container.setAttribute("data-action-preview", name);
                container.append(element("h6", name === "message" ? "Message preview" : `${labelText(name)} preview`));
                const mode = element("select", undefined, "form-select mb-2");
                mode.setAttribute("aria-label", `${labelText(name)} preview data`);
                [{ value: "sample", text: "Sample values" }, { value: "missing", text: "Missing values / fallbacks" }]
                    .forEach(choice => {
                        const option = element("option", choice.text);
                        option.value = choice.value;
                        mode.append(option);
                    });
                container.append(mode, element("p", "Samples are illustrative and may differ from live values.", "form-text"));
                const result = element("div");
                result.setAttribute("aria-live", "polite");
                container.append(result);
                panel.append(container);
                let timer, pending, revision = 0;
                const schedule = () => {
                    clearTimeout(timer);
                    pending?.abort();
                    const current = ++revision;
                    result.replaceChildren(element("p", "Updating preview...", "form-text"));
                    timer = setTimeout(async () => {
                        if (!host.contains(panel)) return;
                        pending = new AbortController();
                        const token = form?.querySelector('[name="__RequestVerificationToken"]')?.value;
                        try {
                            const response = await fetch(root.dataset.previewUrl, {
                                method: "POST",
                                headers: { "Content-Type": "application/json", ...(token ? { RequestVerificationToken: token } : {}) },
                                body: JSON.stringify({
                                    featureId: root.dataset.featureId,
                                    gameServerId: root.dataset.gameServerId || null,
                                    actionTypeId: action.typeId,
                                    parameters: action.parameters,
                                    templateParameters: action.templateParameters ?? [],
                                    parameterName: name,
                                    mode: mode.value
                                }),
                                signal: pending.signal
                            });
                            if (!response.ok) throw new Error(`Preview request failed (${response.status}).`);
                            const preview = await response.json();
                            if (current !== revision || !host.contains(panel)) return;
                            if (!object(preview) || !Array.isArray(preview.errors) || !Array.isArray(preview.diagnostics) ||
                                !Array.isArray(preview.warnings) || !Array.isArray(preview.fragments))
                                throw new Error("Preview response is invalid.");
                            result.replaceChildren();
                            preview.errors.forEach(message => result.append(element("p", message, "alert alert-danger py-1")));
                            preview.diagnostics.forEach(message => result.append(element("p", message, "form-text")));
                            preview.warnings.forEach(message => result.append(element("p", message, "alert alert-warning py-1")));
                            const showText = text => {
                                const output = element("div", undefined, "cod-message-preview mb-2");
                                if (preview.useCodColorCodes !== false && typeof CodColors !== "undefined" && typeof CodColors.renderInto === "function")
                                    CodColors.renderInto(output, text);
                                else output.textContent = text;
                                result.append(output);
                            };
                            if (typeof preview.text === "string") showText(preview.text);
                            preview.fragments.forEach(fragment => {
                                if (!object(fragment) || typeof fragment.text !== "string" ||
                                    !Number.isInteger(fragment.itemIndex) || !Number.isInteger(fragment.fragmentIndex) ||
                                    !Number.isInteger(fragment.utf8ByteCount) || !Number.isInteger(fragment.totalPayloadByteCount))
                                    throw new Error("Preview fragment is invalid.");
                                result.append(element("p", `Message ${fragment.itemIndex + 1}, fragment ${fragment.fragmentIndex + 1}: ${fragment.utf8ByteCount} UTF-8 bytes (${fragment.totalPayloadByteCount} including reserved prefix).`, "form-text"));
                                showText(fragment.text);
                            });
                        } catch (error) {
                            if (error.name !== "AbortError" && current === revision && host.contains(panel))
                                result.replaceChildren(element("p", error.message || "Preview is unavailable.", "alert alert-danger py-1"));
                        }
                    }, 250);
                };
                panel.addEventListener("input", event => { if (!container.contains(event.target)) schedule(); });
                panel.addEventListener("change", event => { if (!container.contains(event.target)) schedule(); });
                mode.addEventListener("change", schedule);
                schedule();
            });
        };
        const renderInheritedRules = parent => {
            const container = element("section", undefined, "mb-4");
            container.append(element("h5", "Inherited global rules (before overrides)"));
            if (!globals.length) container.append(element("p", "No global rules.", "form-text"));
            const describeCondition = (target, condition) => {
                if (!object(condition)) {
                    details(target, "Unrecognized inherited condition", condition);
                } else if (condition.groupOperator != null && Array.isArray(condition.conditions)) {
                    const group = element("div", undefined, "border-start ms-2 ps-3 mb-2");
                    group.append(element("strong", condition.groupOperator === "And" ? "ALL" :
                        condition.groupOperator === "Or" ? "ANY" : `Group: ${condition.groupOperator}`));
                    condition.conditions.forEach(child => describeCondition(group, child));
                    target.append(group);
                } else if (typeof condition.conditionId === "string") {
                    const definition = conditions.find(item => item.conditionId === condition.conditionId);
                    target.append(element("div", definition?.displayName ?? `Unknown condition: ${condition.conditionId}`, "fw-bold"));
                    info(target, condition.conditionId, `Inherited condition ${condition.conditionId}`);
                    details(target, "Condition parameters", condition.parameters);
                } else details(target, "Unrecognized inherited condition", condition);
            };
            globals.forEach((rule, index) => {
                const panel = element("div", undefined, "border rounded p-3 mb-2");
                if (!object(rule)) {
                    details(panel, `Unrecognized global rule ${index + 1}`, rule);
                } else {
                    panel.append(element("h6", `${rule.id ?? `Rule ${index + 1}`} (${rule.enabled === true ? "enabled" : rule.enabled === false ? "disabled" : "enabled state unknown"})`));
                    panel.append(element("strong", "WHEN"));
                    describeCondition(panel, rule.condition);
                    panel.append(element("strong", "THEN"));
                    if (Array.isArray(rule.actions)) rule.actions.forEach(action => {
                        if (!object(action)) { details(panel, "Unrecognized inherited action", action); return; }
                        const definition = actions.find(item => item.actionTypeId === action.typeId);
                        panel.append(element("div", definition?.displayName ?? `Unknown action: ${action.typeId}`, "fw-bold"));
                        info(panel, action.typeId, `Inherited action ${action.typeId}`);
                        details(panel, "Action parameters and dependencies", action);
                    });
                    else details(panel, "Unrecognized inherited actions", rule.actions);
                    panel.append(element("p", `After match: ${rule.afterMatch ?? "unknown"}`, "form-text"));
                }
                container.append(panel);
            });
            parent.append(container);
        };
        const renderActions = (parent, rule) => {
            if (!Array.isArray(rule.actions)) {
                parent.append(element("p", "Malformed actions are preserved.", "alert alert-warning"));
                details(parent, "Stored actions", rule.actions);
                return;
            }
            rule.actions.forEach((action, index) => {
                const panel = element("div", undefined, "border rounded p-3 mb-2");
                parent.append(panel);
                if (!object(action)) {
                    details(panel, "Unsupported action (preserved)", action);
                    panel.append(button("Remove action", () => { rule.actions.splice(index, 1); sync(); render(); }, true));
                    return;
                }
                panel.append(element("h6", `Action ${index + 1}`));
                select(panel, "Action type", action.typeId, actionChoices, id => {
                    const previous = actions.find(item => item.actionTypeId === action.typeId);
                    const next = availableActions.find(item => item.actionTypeId === id);
                    if (!next) return;
                    const extras = object(action.parameters) ? { ...action.parameters } : {};
                    previous?.parameters.forEach(param => delete extras[param.name]);
                    next.parameters.forEach(param => delete extras[param.name]);
                    if (Array.isArray(action.templateParameters)) {
                        const previousNames = previous?.parameters.filter(param => param.supportsTemplates).map(param => param.name) ?? [];
                        const nextNames = next.parameters.filter(param => param.supportsTemplates).map(param => param.name);
                        action.templateParameters = action.templateParameters.filter(name =>
                            !previousNames.includes(name) && nextNames.includes(name));
                    }
                    action.typeId = id; action.parameters = extras;
                    render();
                });
                const definition = actions.find(item => item.actionTypeId === action.typeId);
                gameSupport(panel, definition);
                if (server && action.typeId) info(panel, action.typeId, "Action compatibility");
                if (definition) {
                    renderParameters(panel, action, definition, false);
                    actionPreviews(panel, action, definition);
                }
                else if (action.typeId) details(panel, "Unsupported action (preserved)", action);
                const advanced = element("details", undefined, "mb-3");
                advanced.append(element("summary", "Advanced action settings"));
                advanced.append(element("p", `Action ID: ${action.id ?? "missing"}`, "form-text"));
                if (Array.isArray(action.dependsOnActionIds)) {
                    const earlier = rule.actions.slice(0, index).map((earlierAction, earlierIndex) => {
                        if (!object(earlierAction) || typeof earlierAction.id !== "string") return null;
                        const earlierDefinition = actions.find(item => item.actionTypeId === earlierAction.typeId);
                        return { value: earlierAction.id, text: `Action ${earlierIndex + 1}: ${earlierDefinition?.displayName ?? earlierAction.typeId ?? "Unknown"}` };
                    }).filter(Boolean);
                    select(advanced, "Depends on earlier actions", action.dependsOnActionIds, earlier, value => { action.dependsOnActionIds = value; }, true);
                } else details(advanced, "Malformed dependencies (preserved)", action.dependsOnActionIds);
                panel.append(advanced);
                extensions(panel, action, ["id", "typeId", "parameters", "dependsOnActionIds", "templateParameters"]);
                panel.append(button("Move action up", () => move(rule.actions, index, -1)));
                panel.append(button("Move action down", () => move(rule.actions, index, 1)));
                panel.append(button("Remove action", () => { rule.actions.splice(index, 1); sync(); render(); }, true));
            });
            if (rule.actions.length < 32) parent.append(button("Add action", () => { rule.actions.push(newAction()); sync(); render(); }, false, !actionChoices.length));
        };
        const render = () => {
            host.replaceChildren();
            if (error) {
                host.append(element("p", error, "alert alert-danger"));
                const raw = element("textarea", undefined, "form-control");
                raw.rows = 6; raw.value = hidden.value;
                raw.addEventListener("input", () => { hidden.value = raw.value; });
                host.append(raw, button("Load JSON", () => {
                    try {
                        const parsed = JSON.parse(raw.value);
                        if (!Array.isArray(parsed)) throw new Error("Rules must be a JSON array.");
                        if (!Array.isArray(globals)) throw new Error("Global rules must be a JSON array.");
                        rules = parsed; error = ""; sync(); render();
                    } catch (e) { error = `Unable to load rules: ${e.message}`; render(); }
                }));
                return;
            }
            if (!feature) host.append(element("p", "This feature is not in the catalogue. Existing data will be preserved.", "alert alert-warning"));
            if (server && root.dataset.showInheritedRules === "true") renderInheritedRules(host);
            rules.forEach((rule, index) => {
                const panel = element("section", undefined, "border rounded p-3 mb-3");
                panel.append(element("h5", `${override ? "Rule override" : "Rule"} ${index + 1}`));
                host.append(panel);
                if (!object(rule)) {
                    details(panel, "Unsupported entry (preserved)", rule);
                    panel.append(button("Remove rule", () => { rules.splice(index, 1); sync(); render(); }, true));
                    return;
                }
                if (override) {
                    const choices = globals.filter(object).filter(r => typeof r.id === "string").map(r => ({ value: r.id, text: r.id }));
                    select(panel, "Global rule", rule.ruleId, choices, value => { rule.ruleId = value; render(); });
                    const inherited = globals.find(item => object(item) && item.id === rule.ruleId);
                    if (inherited) {
                        inheritedCompatibility(panel, inherited, rule);
                        details(panel, "Global rule inherited values", inherited);
                    }
                    select(panel, "Enabled", rule.enabled == null ? "inherit" : rule.enabled ? "true" : "false",
                        [{ value: "inherit", text: "Inherit" }, { value: "true", text: "Enabled" }, { value: "false", text: "Disabled" }],
                        value => { rule.enabled = value === "inherit" ? null : value === "true"; });
                } else {
                    input(panel, "Rule ID", rule.id, value => { rule.id = value; }, "text", true);
                    check(panel, "Enabled", rule.enabled, value => { rule.enabled = value; });
                }
                const section = (key, title, create, draw) => {
                    const noChoices = key === "condition" ? !conditionChoices.length : key === "actions" ? !actionChoices.length : false;
                    if (override) check(panel, `Override ${title}`, rule[key] != null, checked => {
                        rule[key] = checked ? create() : null; render();
                    }, noChoices && rule[key] == null);
                    if (!override || rule[key] != null) {
                        panel.append(element("h6", title));
                        draw();
                    }
                };
                section("condition", "WHEN", newLeaf, () => renderCondition(panel, rule.condition, value => { rule.condition = value; }));
                section("actions", "THEN", () => [newAction()], () => renderActions(panel, rule));
                section("afterMatch", "After match", () => "StopProcessingRules", () =>
                    select(panel, "Continue processing", rule.afterMatch, ["StopProcessingRules", "ContinueToNextRule"], value => { rule.afterMatch = value; }));
                if (root.dataset.featureId === "welcome-messages") {
                    const effective = effectiveTimings?.[override ? rule.ruleId : rule.id];
                    if (effective && Number.isInteger(effective.connectionDelaySeconds) && Number.isInteger(effective.freshnessSeconds))
                        panel.append(element("p", `Effective at page load: ${effective.connectionDelaySeconds} second delay; ${effective.freshnessSeconds} second freshness. Save changes to refresh.`, "form-text"));
                    if (rule.timing != null && !object(rule.timing)) {
                        panel.append(element("p", "Malformed timing is preserved. Replace this rule to edit it.", "alert alert-warning"));
                        details(panel, "Stored timing", rule.timing);
                    } else {
                        const timing = rule.timing ?? {};
                        const timingField = (name, title, min, max) => {
                            const control = input(panel, title, timing[name], (value, node) => {
                                if (node.validity.badInput) {
                                    return;
                                } else if (value === "") {
                                    delete timing[name];
                                    if (Object.keys(timing).length === 0) delete rule.timing;
                                } else {
                                    timing[name] = Number(value);
                                    rule.timing = timing;
                                }
                            }, "number");
                            control.dataset.ruleTiming = "";
                            control.min = min;
                            control.max = max;
                            control.step = "1";
                            control.placeholder = "Inherit";
                        };
                        timingField("connectionDelaySeconds", "Connection delay override (seconds)", "0", "60");
                        timingField("freshnessSeconds", "Freshness override (seconds)", "1", "900");
                    }
                }
                const ruleKeys = override ? ["ruleId", "enabled", "condition", "actions", "afterMatch"] :
                    ["id", "enabled", "order", "condition", "actions", "afterMatch"];
                if (root.dataset.featureId === "welcome-messages") ruleKeys.push("timing");
                extensions(panel, rule, ruleKeys);
                panel.append(button("Move rule up", () => { move(rules, index, -1); if (!override) { rules.forEach((r, i) => { if (object(r)) r.order = i; }); sync(); } }));
                panel.append(button("Move rule down", () => { move(rules, index, 1); if (!override) { rules.forEach((r, i) => { if (object(r)) r.order = i; }); sync(); } }));
                panel.append(button("Remove rule", () => { rules.splice(index, 1); if (!override) rules.forEach((r, i) => { if (object(r)) r.order = i; }); sync(); render(); }, true));
            });
            if (override && !globals.some(item => object(item) && typeof item.id === "string" && item.id))
                host.append(element("p", "No global rules are available to override.", "form-text"));
            if (server && (!conditionChoices.length || !actionChoices.length))
                host.append(element("p", "New conditions and actions require verified server compatibility.", "alert alert-warning"));
            if (feature && rules.length < 100 && (!override || globals.some(item => object(item) && typeof item.id === "string" && item.id))) host.append(button(override ? "Add rule override" : "Add rule", () => {
                rules.push(override ? { ruleId: globals.find(object)?.id ?? "", enabled: null, condition: null, actions: null, afterMatch: null } :
                    { id: crypto.randomUUID(), enabled: true, order: rules.length, condition: newLeaf(), actions: [newAction()], afterMatch: "StopProcessingRules" });
                sync(); render();
            }, false, !override && (!conditionChoices.length || !actionChoices.length)));
        };
        if (form) form.addEventListener("submit", event => {
            if (error) {
                event.preventDefault();
                host.querySelector("textarea")?.focus();
            } else sync();
        });
        render();
    });
})();
