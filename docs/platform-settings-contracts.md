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

### VPN observation forum posting

VPN rules expose `postObservationToForums` through **Post observations to forums** in
the global and server-local rule editors. Set it to `false` to retain the portal
observation, automation deduplication, server event, and audit without creating or
linking a forum topic. Missing values default to `true` for backward compatibility.
Server rule overrides use `null` to inherit and `true`/`false` to override.

The strongest matching action wins (Ban, Kick, Observation); ties use rule order.
The winning rule supplies the observation publication preference. Kick and Ban
always retain forum publication regardless of this observation-only setting.
Existing observations and forum topics are not changed or backfilled.

### VPN and Siege rule conditions

VPN Protection schema 2 uses nested AND/OR conditions, with VPN-specific signals
and actions. Siege Mode uses the same condition editor and retains its player
history and tag signals. Server VPN rules can replace a global rule's entire
condition or inherit it; other override fields remain independent.

Schema 1 VPN rules are not migrated. Replace the global configuration with a
disabled, empty schema 2 document and remove old server overrides as part of the
coordinated cutover. The settings editor rejects unsupported saved schemas rather
than rewriting them. Publish the shared contracts package and coordinate the
repository, event processor, cache expiry, and portal deployment before cutover.
Do not activate Siege Mode as part of this change.

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
