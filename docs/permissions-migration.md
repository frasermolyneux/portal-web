# Permissions Migration Guide

This document maps the legacy authorization policy names to the new `{Domain}.{Action}` naming convention introduced in the permissions refactor.

## Overview of Changes

- **Consolidated from 67 policies to 47** — removed redundant per-action policies, merged related capabilities
- **All handlers check both role claims AND direct permission grants** (additional permissions)
- **Additional permissions now use `{Domain}.{Action}` claim types** with game/server scoping via `PermissionScope`
- **Server-side validation** rejects invalid claim types and scope mismatches on create/set endpoints
- **5 policies removed** entirely (now plain `[Authorize]` — any authenticated user)

---

## Old → New Policy Name Mapping

### Admin Actions

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessAdminActionsController` | `AdminActions.Read` |
| `CreateAdminAction` | `AdminActions.Create` |
| `EditAdminAction` | `AdminActions.Edit` |
| `DeleteAdminAction` | `AdminActions.Delete` |
| `ClaimAdminAction` | `AdminActions.Claim` |
| `LiftAdminAction` | `AdminActions.Lift` |
| `ChangeAdminActionAdmin` | `AdminActions.Reassign` |
| `CreateAdminActionTopic` | `AdminActions.CreateTopic` |

### Players

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessPlayers` | `Players.Read` |
| `ViewPlayers` | `Players.Read` |
| `DeletePlayer` | `Players.Delete` |
| `CreateProtectedName` | `Players.ProtectedNames.Write` |
| `DeleteProtectedName` | `Players.ProtectedNames.Write` |
| `ViewProtectedName` | `Players.Read` |

### Player Tags

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessPlayerTags` | `Tags.Read` |
| `CreatePlayerTag` | `Tags.Write` |
| `EditPlayerTag` | `Tags.Write` |
| `DeletePlayerTag` | `Tags.Write` |

### Game Servers — Core

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessGameServers` | `GameServers.Read` |
| `ViewGameServer` | `GameServers.Read` |
| `CreateGameServer` | `GameServers.Write` |
| `EditGameServer` | `GameServers.Write` |
| `DeleteGameServer` | `GameServers.Delete` |

### Game Servers — Credentials

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessCredentials` | `GameServers.Credentials.Ftp.Read` or `GameServers.Credentials.Rcon.Read` |
| `EditGameServerFtp` | `GameServers.Credentials.Ftp.Write` |
| `EditGameServerRcon` | `GameServers.Credentials.Rcon.Write` |
| `ViewFtpCredential` | `GameServers.Credentials.Ftp.Read` |
| `ViewRconCredential` | `GameServers.Credentials.Rcon.Read` |

### Game Servers — Maps

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessMapManagerController` | `GameServers.Maps.Read` |
| `ManageMaps` | `GameServers.Maps.Read` |
| `PushMapToRemote` | `GameServers.Maps.Deploy` |
| `DeleteMapFromHost` | `GameServers.Maps.Deploy` |

### Game Servers — Ban File Monitors

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessBanFileMonitors` | `GameServers.BanFileMonitors.Read` |
| `ViewBanFileMonitor` | `GameServers.BanFileMonitors.Read` |
| `CreateBanFileMonitor` | `GameServers.BanFileMonitors.Write` |
| `EditBanFileMonitor` | `GameServers.BanFileMonitors.Write` |
| `DeleteBanFileMonitor` | `GameServers.BanFileMonitors.Write` |

### Game Servers — Admin / RCON

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessServerAdmin` | `GameServers.Admin.Read` |
| `AccessLiveRcon` | `GameServers.Admin.Rcon` |
| `ViewLiveRcon` | `GameServers.Admin.Rcon` |
| _(new — no old equivalent)_ | `GameServers.Admin.Rcon.Kick` |
| _(new — no old equivalent)_ | `GameServers.Admin.Rcon.Ban` |
| _(new — no old equivalent)_ | `GameServers.Admin.Rcon.Map` |
| _(new — no old equivalent)_ | `GameServers.Admin.Rcon.Say` |
| _(new — no old equivalent)_ | `GameServers.Admin.Rcon.Restart` |

### Chat Log

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `ViewGlobalChatLog` | `ChatLog.Read` |
| `ViewGameChatLog` | `ChatLog.Read` |
| `ViewServerChatLog` | `ChatLog.ReadServer` |
| `LockChatMessages` | `ChatLog.Lock` |

### Maps (Catalogue)

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessMaps` | `Maps.Read` |

### Map Rotations

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| _(new — no old equivalent)_ | `MapRotations.Read` |
| _(new — no old equivalent)_ | `MapRotations.Write` |
| _(new — no old equivalent)_ | `MapRotations.Deploy` |

### Dashboard

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| _(new — no old equivalent)_ | `Dashboard.Read` |

### Demos

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| _(new — no old equivalent)_ | `Demos.Read` |
| _(new — no old equivalent)_ | `Demos.Write` |
| _(new — no old equivalent)_ | `Demos.Delete` |

### Users / System (not assignable as additional permissions)

| Old Policy Name | New Policy Name |
|----------------|-----------------|
| `AccessUsers` | `Users.Read` |
| `PerformUserSearch` | `Users.Search` |
| `CreateUserClaim` | `Users.ManageClaims` |
| `DeleteUserClaim` | `Users.ManageClaims` |
| `AccessStatus` | `GlobalSettings.Admin` |
| `AccessMigration` | `GlobalSettings.Admin` |
| _(new — no old equivalent)_ | `Users.ActivityLog` |

---

## Old → New Claim Type Mapping

Legacy claim types in `UserProfileClaimType` have been superseded by `AdditionalPermission` constants:

| Old Claim Type | New Claim Type | Notes |
|---------------|---------------|-------|
| `FtpCredentials` | `GameServers.Credentials.Ftp.Read` | Server-scoped → GameOrServer-scoped |
| `RconCredentials` | `GameServers.Credentials.Rcon.Read` | Server-scoped → GameOrServer-scoped |
| `GameServer` | `GameServers.Read` | Game-type-scoped → GameOrServer-scoped |
| `BanFileMonitor` | `GameServers.BanFileMonitors.Read` | Server-scoped → GameOrServer-scoped |
| `ServerAdmin` | `GameServers.Admin.Read` | Game-type-scoped (unchanged) |
| `LiveRcon` | `GameServers.Admin.Rcon` | Game-type-scoped (unchanged) |
| `FileMonitor` | _(removed, unused)_ | Was never referenced in handlers |
| `RconMonitor` | _(removed, unused)_ | Was never referenced in handlers |

The old claim types are marked `[Obsolete]` in `UserProfileClaimType` but still function during the transition period. Authorization handlers recognize both old and new claim types. New claim assignments should use the `AdditionalPermission` constants exclusively.

---

## Removed Policies (now plain `[Authorize]`)

These policies were removed because they protected pages accessible to any authenticated user. They are now gated by a simple `[Authorize]` attribute (any logged-in user):

| Removed Policy | Former Location |
|---------------|----------------|
| `AccessHome` | Home controller |
| `AccessServers` | Servers list page |
| `AccessChangeLog` | Change log page |
| `AccessProfile` | User profile page |
| `AccessCredentials` | Credentials page (replaced by specific FTP/RCON read policies) |

---

## Key Changes Summary

1. **Naming convention**: All policies now follow `{Domain}.{Action}` (e.g., `AdminActions.Create` instead of `CreateAdminAction`).

2. **Policy consolidation**: Related old policies merged into single new policies:
   - `CreateProtectedName` + `DeleteProtectedName` → `Players.ProtectedNames.Write`
   - `CreatePlayerTag` + `EditPlayerTag` + `DeletePlayerTag` → `Tags.Write`
   - `CreateUserClaim` + `DeleteUserClaim` → `Users.ManageClaims`
   - `ViewGlobalChatLog` + `ViewGameChatLog` → `ChatLog.Read`
   - `AccessLiveRcon` + `ViewLiveRcon` → `GameServers.Admin.Rcon`

3. **New granular RCON policies**: RCON operations split into 5 sub-permissions (`Kick`, `Ban`, `Map`, `Say`, `Restart`) for fine-grained control.

4. **New domains**: Map Rotations, Dashboard, and Demos are new permission domains with no old equivalents.

5. **Dual-path authorization**: Every handler now checks both role claims (from forum sync) and additional permission claims. Previously, some handlers only checked role claims.

6. **Scope validation**: The repository API now validates that claim values match the permission's `PermissionScope` (Game, Server, or GameOrServer), rejecting mismatches at write time.

7. **43 assignable permissions**: Of the 47 total policies, 43 can be assigned as additional permissions. The remaining 4 (`GlobalSettings.Admin`, `Users.Read`, `Users.ManageClaims`, `Users.Search`, `Users.ActivityLog`) are role-gated only.
