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

Release the updated `XtremeIdiots.Portal.Settings.Contracts.V1` package from
`portal-repository` before deploying its consumers. The consumer changes target
`4.2.70`; align that pin with the actual generated release version if necessary.
Deploy `portal-server-events` before `portal-web` so the processor understands the
setting before operators can save it. Then disable forum posting on the desired
production observation rules; existing rules otherwise continue posting.

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
