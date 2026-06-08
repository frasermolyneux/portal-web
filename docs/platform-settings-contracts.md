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
