# Platform Settings Contracts

This document describes the current settings architecture used by `portal-web` after the Platform Settings contracts migration.

## Architecture

- Source of truth for typed contracts and validators: `XtremeIdiots.Portal.Settings.Contracts.V1`.
- Persistence and transport remain dynamic in `portal-repository` as `namespace + configuration (JSON string)`.
- `portal-web` maps view models to typed documents in `Services/Settings/NamespaceSettingsSerializer.cs`.
- `portal-web` reads typed documents from repository payloads in `Services/Settings/NamespaceSettingsParser.cs`.
- Controllers orchestrate save/load and must not implement ad hoc namespace-specific JSON parsing.

## Migration Summary

- Old approach: controller/runtime paths performed raw namespace JSON handling with local schema assumptions.
- New approach: typed namespace contracts + validators are centralized in `XtremeIdiots.Portal.Settings.Contracts.V1`.
- Compatibility policy:
  - Legacy payload shapes can be tolerated by contract-level compatibility behavior where supported.
  - The legacy `XtremeIdiots.Portal.ChatCommands.Abstractions.V1` path is compatibility-only and not the canonical source for new settings work.

## Troubleshooting Runbook

### Broadcast messages

The server editor resolves saved effective settings with the shared broadcast
resolver. Enablement, interval and message source can inherit independently.
An absent server namespace inherits global messages; an existing legacy
namespace with no message mode uses its server list, even when empty. Selecting
an empty server list explicitly replaces the global list with no messages.
Resetting to global retains server messages for later use.

Unsupported or incomplete broadcast documents and incomplete form submissions
are preserved when other settings are saved. Effective values are unavailable
when the saved global or server document cannot be interpreted safely. Older
agents that lack inheritance default to disabled when no server settings exist.

### Chat commands

The global and server editors make chat-command settings read-only when the
saved document contains fields or values they cannot preserve, including newer
command policies and replies. Saving other settings leaves that namespace
unchanged. Editing those settings requires a compatible editor; the read-only
warning does not indicate whether a command is enabled at runtime.

### Moderation

The global and server editors make the existing moderation namespace read-only
when its saved document contains unsupported fields or values, or any of the
section's fields are omitted from a submission. Saving other settings leaves
that namespace unchanged, including protected-name settings. This does not affect
a separate rules namespace.

### VPN and Siege rules

VPN Protection and Siege Mode use the shared rules-engine contracts and editor.
Rules contain nested AND/OR conditions, ordered actions and a feature-scoped
stop/continue choice. Forum publication is configured on record-producing
actions, rather than on the rule itself. Unknown conditions are retained and
evaluate as unknown; unsupported actions are retained but cannot execute.

Global settings provide feature enablement and rules. Each server independently
overrides enablement, chooses whether to inherit global rules, and can configure
local rules and overrides of individual global rules. Inherited global rules run
before local rules; disabling inheritance retains but ignores the global
overrides. Siege duration is an independent global setting and server override.

This is a clean-slate settings change: older VPN and Siege rule documents are
not migrated or read by the new editor. Coordinate the settings reset and
repository, runtime and portal deployments before the production cutover.

### Welcome rules

Welcome Messages uses the shared editor and the `welcomeRules` namespace;
legacy `welcomeMessages` documents are neither migrated nor rewritten by the
editor. Coordinate their removal with the runtime cutover before enabling the
new feature. Server rules are available independently of the agent toggle.

Connection delay and freshness inherit from global settings, with server and
per-rule overrides resolved by the shared merger. Message and reason templates
are opt-in; literal text remains literal. The ordered message list follows the
single message when both are supplied. Preview uses the shared renderer and
segmenter with sample or missing values. Server previews use the RCON channel
budget and character constraints when available, caching the lookup for ten
seconds; otherwise they retain the budget-unavailability warning and use an
interim 149-byte budget without a prefix. Global previews have no selected server
and use the same interim budget. Neither preview adds an estimate disclaimer.
A later trigger may use another
provider or a changed prefix, so runtime-frozen fragments remain authoritative.
Both segmentation and display honor the returned budget's CoD colour mode;
literal caret-digit text is retained when that mode is disabled. This does not
enable any additional rules-native provider.

**Rules Engine Audit** under Server Admin is protected by the global settings
admin policy. It queries executions across servers, with server, feature,
match-status and UTC created-time filters, 20 at a time. The time bounds are
inclusive from/exclusive to. Executions can be reused for a connection and do
not represent every event or action; other features may use separate histories.

Supported typed evidence shows recorded rule, condition and check results and
explicit not-run or skipped action decisions. Action claims and outcomes take
precedence over earlier decisions. Missing, omitted or unsupported evidence does
not prove that an action did not run; a no-match diagnostic alone may describe
an earlier attempt. Dispatched and unknown outcomes do not confirm application,
and dry-run outcomes are simulated. Typed diagnostics and prepared action
windows remain available without parsing the processor's opaque snapshot.
Waiting text is current only while the execution state is `Waiting`.

### Settings loading and runtime behavior

1. A settings page fails to load expected values.
   - Confirm namespace payload exists in repository APIs.
   - Confirm parser path uses typed contracts (`NamespaceSettingsParser`) for the namespace.
   - Check for schema-version mismatch in logs and validate against contracts package supported versions.

2. Save succeeds but runtime behavior is disabled unexpectedly.
   - Validate dependency toggles in `GameServers/Edit` follow disable-only behavior.
   - Confirm authored values were preserved when prerequisites were disabled.

3. Chat command or welcome message behavior diverges from server processors.
   - Verify both producer and consumer repos are pinned to the same published `XtremeIdiots.Portal.Settings.Contracts.V1` version.
   - Confirm no new code references `XtremeIdiots.Portal.ChatCommands.Abstractions.V1` as a canonical contract source.
