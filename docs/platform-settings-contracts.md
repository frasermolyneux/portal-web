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
seconds; otherwise they show a labelled interim estimate without a prefix.
Global previews have no selected server. A later trigger may use another
provider or a changed prefix, so runtime-frozen fragments remain authoritative.
Both segmentation and display honor the returned budget's CoD colour mode;
literal caret-digit text is retained when that mode is disabled. This does not
enable any additional rules-native provider.

The server welcome settings link to **Welcome execution diagnostics**, protected
by the same game-scoped server-write policy. The page defaults to the last 24
hours and queries 20 executions at a time, including waiting and no-action
executions. UTC created-time bounds are inclusive from/exclusive to. Typed
diagnostic codes expose recorded waiting, protection, expiry and token fallback
reasons; `DiagnosticsOmitted` is shown as a warning with the omitted count.
Prepared action windows, frozen fragments and individual outcomes provide
delivery evidence without parsing the processor's opaque decision snapshot.
Waiting text is current only while the execution state is `Waiting`; empty
diagnostics or action collections do not imply no match or suppression.

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
