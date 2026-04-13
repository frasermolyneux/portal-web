# Authorization Model

This document explains **how** the portal's authorization model works. For the exact role-to-permission mappings, the implementation is the source of truth — see the [Key Implementation Files](#key-implementation-files) section.

## Policy Naming Convention

All policies follow a `{Domain}.{Action}` naming pattern (e.g., `AdminActions.Create`, `GameServers.Credentials.Ftp.Read`). Policy name constants are defined in `AuthPolicies.cs` and registered via `PolicyExtensions.AddXtremeIdiotsPolicies()`. Each policy maps to a marker requirement class and a corresponding authorization handler.

## Role Hierarchy

Role claims are synced from the forum by the `portal-sync` service and follow this hierarchy (highest to lowest):

1. **SeniorAdmin** — global access across all domains and game types
2. **HeadAdmin** — game-type scoped; inherits GameAdmin capabilities plus elevated actions (reassign, lift without ownership, credential access)
3. **GameAdmin** — game-type scoped; standard admin actions
4. **Moderator** — game-type scoped; limited to observations, warnings, kicks, and chat viewing

Higher roles implicitly inherit the capabilities of lower roles within their game type scope. SeniorAdmin is the only role with unrestricted global access.

## Dual-Path Authorization

Every authorization handler checks **both** paths, and access is granted if **either** succeeds:

1. **Role claims** — Automatically synced from forum group membership. These provide baseline access scoped to one or more game types.
2. **Additional permissions** — Manually assigned `{Domain}.{Action}` claim types with game or server scoping. These allow fine-grained access beyond a user's role (e.g., granting `GameServers.Admin.Rcon` for a specific game without a full GameAdmin role).

A user's effective permissions are the **union** of their role-based access and any directly granted additional permissions.

## Resource-Based Scoping

Authorization checks are not simply "does the user have role X." Handlers evaluate the **resource context** passed alongside the policy, which determines the scope of the check:

### Game-Type Scoped

Most policies are scoped to a `GameType`. A HeadAdmin for COD4 can only exercise HeadAdmin-level permissions against COD4 resources, not resources belonging to other game types.

### Server Scoped

Some additional permissions are scoped to a specific game server (identified by GUID). For example, a user can be granted `GameServers.Credentials.Ftp.Read` for a single server without access to FTP credentials for other servers in the same game type. Server-scoped checks typically appear as `(GameType, Guid)` resource tuples.

### Ownership-Based

Certain actions require the user to be the **owner** (creator) of the resource. For example:
- A GameAdmin can edit or lift their **own** admin actions but not those created by other admins.
- A user can delete their **own** uploaded demos.

HeadAdmin and SeniorAdmin are generally exempt from ownership requirements.

### Composite Checks

Handlers often combine multiple checks. For example, `AdminActions.Edit` checks:
1. SeniorAdmin (always succeeds), then
2. HeadAdmin for the game type (succeeds without ownership), then
3. GameAdmin/Moderator for the game type **and** ownership of the specific action, then
4. Direct permission grant as a fallback

## PolicyTagHelper

The `PolicyTagHelper` enables policy-based conditional rendering in Razor views:

```html
<div policy="@AuthPolicies.GameServers_Read">Only visible with GameServers.Read</div>
<a policy="@AuthPolicies.AdminActions_Create" policy-resource="@gameType">Create Action</a>
```

The `policy-resource` attribute passes a resource (typically a `GameType`) to handlers for scoped authorization checks. Elements are suppressed entirely if authorization fails.

## Potential Access Checks (PotentialAccessProbe)

Resource-scoped policies require a concrete resource (e.g., `GameType`) to evaluate role-based access. But some scenarios need to answer **"can this user potentially perform this action for ANY resource?"** — for example, showing a Create button on an Index page before a specific game type is known.

### The Problem

Calling `AuthorizeAsync` without a resource leaves the resource as `null`. Handlers treat `null` as **fail-closed** — role-based branches that need a `GameType` do nothing, so only SeniorAdmin and direct permission holders pass. This is correct for security (missing resources should deny, not allow), but too restrictive for UI gating.

### The Solution

Pass `PotentialAccessProbe.Instance` as the resource. Handlers recognise this sentinel and check whether the user holds **any** game-scoped role that could satisfy the policy:

```csharp
// Controller — gate a GET Create action
var canCreate = await authorizationService.AuthorizeAsync(
    User, PotentialAccessProbe.Instance, AuthPolicies.MapRotations_Write);
if (!canCreate.Succeeded) return Forbid();
```

```html
<!-- View — conditionally render a Create button -->
<a policy="@AuthPolicies.MapRotations_Write"
   policy-resource="@PotentialAccessProbe.Instance">Create</a>
```

### How It Works

Each composite method in `BaseAuthorizationHelper` has three branches:

| Resource | Behaviour |
|----------|-----------|
| Concrete (`GameType`, tuple) | Checks role for that specific game type |
| `PotentialAccessProbe` | Checks if user holds any game-scoped role |
| `null` | Does nothing (fail-closed) |

`CheckDirectPermissionGrant` already handles all three — it checks for a scoped permission when a resource is present, and for **any** permission claim when no resource is provided.

### When to Use Each Pattern

| Scenario | Resource to Pass | Example |
|----------|-----------------|---------|
| Action on a known resource | The resource (`GameType`, `(GameType, Guid)`, etc.) | Edit button in a Details view |
| UI gate before resource exists | `PotentialAccessProbe.Instance` | Create button on Index page, GET Create action |
| Non-resource-scoped policy | Nothing (omit `policy-resource`) | Tags.Write, Dashboard.Read |
| Data filtering (what to show) | N/A — use `ClaimedGamesAndItems` | Index action fetching records |

## Key Implementation Files

These files are the **source of truth** for all authorization behaviour:

| File | Purpose |
|------|---------|
| `Auth/Constants/AuthPolicies.cs` | All policy name constants |
| `Auth/Requirements/AuthRequirements.cs` | Marker requirement classes (one per policy) |
| `Auth/PotentialAccessProbe.cs` | Sentinel resource for "can user potentially do X?" checks |
| `Auth/Handlers/BaseAuthorizationHelper.cs` | Shared claim group definitions and common check methods |
| `Auth/Handlers/*AuthHandler.cs` | Per-domain authorization handlers with exact role→permission logic |
| `Extensions/PolicyExtensions.cs` | Policy registration wiring |
| `Helpers/PolicyTagHelper.cs` | Razor tag helper for conditional rendering |

All paths are relative to `src/XtremeIdiots.Portal.Web/`.
