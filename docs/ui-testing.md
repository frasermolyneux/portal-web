# UI Testing

The portal integration suite runs the ASP.NET Core application locally with deterministic test configuration, an in-memory SQLite identity database, test-only authentication profiles, and in-process fake API clients. Playwright connects to Kestrel on an operating-system assigned loopback port. No Azure login, Docker daemon, SQL Server, external API, or manually started web process is required.

## Run locally

Run the `dotnet: test-integration` VS Code task. The first run restores packages and downloads the pinned Chromium build; later runs reuse local caches.

The equivalent command is:

```powershell
pwsh ./scripts/run-ui-tests.ps1
```

Unit tests remain available through `dotnet: test` and exclude the integration project by the existing `FullyQualifiedName!~IntegrationTests` filter.

## Test structure

- `Hosting/` builds isolated TestServer and Kestrel application hosts from the production `PortalWebApplication` composition.
- `Authentication/` defines named role profiles delivered through the test-only `X-Portal-Test-Profile` header.
- `Authorization/` contains the executable policy matrix for all 53 policies and five baseline roles.
- `Health/` verifies required liveness, readiness, and version endpoints.
- `Manifest/` discovers and classifies every MVC/API action and enforces the approved application surface.
- `Playwright/` verifies rendered pages, browser behavior, policy-controlled UI, and direct authorization enforcement.
- `Workflows/` contains Reqnroll `.feature` specifications, domain scenarios, and Playwright step bindings for state-changing features.

Browser tests reject unexpected external requests and fail on same-origin request failures, HTTP error responses, console errors, and page errors. Known cosmetic CDN styles are omitted in the isolated environment.

## Adding coverage

Use HTTP integration tests for broad routing, endpoint, and Razor rendering coverage. Use Playwright when the behavior depends on browser rendering, JavaScript, navigation visibility, or a complete user workflow.

Prefer accessible selectors by role, label, and visible text. Add `data-testid` only when the control has no stable accessible selector. Razor changes must follow `docs/ui-standards-guide.md`.

Authorization tests must keep real policies and handlers active. Add test identities or scenario data through the integration project rather than adding production test-login endpoints or credentials.

## Authorization matrix

`AuthorizationMatrix` is the executable authorization specification. Every policy is tested through the real `IAuthorizationService` for Anonymous, Moderator, GameAdmin, HeadAdmin, and SeniorAdmin. Resource-sensitive policies add scenarios for ownership, action type, game/server scope, direct permissions, COD4/COD4x equivalence, and `PotentialAccessProbe`.

Adding an `AuthPolicies` constant without a registered policy and matrix entry fails the integration suite. System-only policies must be included in the explicit non-assignable list.

## Action manifest

`PortalActionManifest` reads ASP.NET Core's runtime `ControllerActionDescriptor` collection. The approved baseline currently contains 246 actions classified as browser pages, HTTP endpoints, state changes, downloads/streams, or external callbacks.

Adding, removing, rerouting, or reclassifying an action changes the manifest fingerprint and fails the suite. The failure writes `portal-actions.actual.txt` beside the integration-test assembly. Review that file and the classification counts before updating `ApprovedFingerprint` and `ApprovedCounts`; never update the fingerprint without reviewing the generated action list.

Browser pages that require seeded identifiers or domain-specific fake responses are implemented as Phase 3 workflow scenarios. Deterministic view-only pages remain in the fast `PageSmokeIntegrationTests` set.

## Workflow packs

Phase 3 browser workflows are executable Gherkin specifications powered by Reqnroll and xUnit. Each `.feature` file owns readable Given/When/Then scenarios, while its `*Steps.cs` binding class performs Playwright interactions and assertions. Generated feature code is written below `obj/` and is not committed.

Each workflow scenario replaces only the dependencies owned by that test host and records state-changing client calls in thread-safe queues. Reqnroll creates binding instances per scenario, and an async `AfterScenario` hook disposes the browser fixture. All `@workflow` features are marked non-parallelizable through `reqnroll.json` because each scenario owns a Chromium process and Kestrel host. Steps assert the browser result and exact downstream DTO rather than sharing mutable global mocks.

Run a workflow pack independently through its feature tag:

```powershell
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=admin-actions"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=tags"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=game-servers"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=say-command"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=map-control"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=player-moderation"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=server-feed"
dotnet test src/XtremeIdiots.Portal.Web.IntegrationTests/XtremeIdiots.Portal.Web.IntegrationTests.csproj --filter "Category=cod4x-lifecycle"
```

When adding a workflow, place its feature, bindings, and scenario fake together under `Workflows/<Domain>/`. Use scenario outlines for behavior permutations, keep technical setup out of feature wording, and use domain-specific step phrases because bindings are global within the Reqnroll project.

Current Pack B coverage includes:

- Admin Action creation for SeniorAdmin and Moderator roles, direct Ban denial, rich-text reason validation, repository command and notification payloads, success navigation, and repository failure behavior.
- Tag definition create, edit, and user-defined delete workflows for GameAdmin, plus direct create denial for Moderator.

Current Pack C coverage includes:

- RCON credential rotation, password visibility, blank-password preservation, server-side validation, scoped write denial, exact namespace/password orchestration, and configuration failure feedback. Production RCON JSON shape is covered by `NamespaceSettingsSerializerTests`.
- Game-server deletion success, SeniorAdmin-only direct access enforcement, exact delete commands, and repository failure feedback.
- FTP/SFTP transport switching, complete SFTP credential rotation, blank-secret preservation, fingerprint/path validation, scoped credential denial, password visibility, exact namespace/value orchestration, and repository failure feedback. Production FTP/SFTP JSON shapes are covered by `NamespaceSettingsSerializerTests`.

Current Pack D coverage includes:

- Live Say broadcasts for direct permission and GameAdmin access, exact trimmed RCON payloads, server-side validation, Moderator UI and forged-request denial, and backend failure feedback.
- Map loading, restart, fast restart, and next-map commands; the separate server-restart permission; direct-grant UI boundaries; forged restart denial; exact CoD4 RCON dispatch; and backend failure feedback.
- Connected-player Kick, TempBan, and Ban actions; independent direct Kick/Ban grants; Moderator Kick access; canonical live slot/GUID/name binding; forged identity, stale-slot, mismatched-search, and HTML-bearing-name defenses; exact CoD4 commands; admin-action persistence; and explicit RCON-success/persistence-failure feedback.
- Unified server feed rendering and HTML safety, event filtering, source-toggle cursor resets, pause/resume buffering, item deduplication, overrun notices, background-page suppression, overlap prevention, forced reload supersession, and disposal.
- CoD4x plugin install, rollback, and unload request contracts; runtime-state preservation; Linux artifact metadata; direct and role authorization; client/server validation; malformed/unavailable settings; and repository queue failures.

These workflows also guard degraded player-tag rendering, Development runtime compilation of the Admin Actions view component, and server-side validation of visible Summernote text. Other Phase 3 domain packs remain separate and can be added under `Workflows/<Domain>/` without changing the shared host.

## Bug-hunting playbook

Use the suite as an executable investigation tool rather than writing broad browser coverage first:

1. Start from the owning controller, Razor view, JavaScript module, authorization handler, and typed-client call. State one falsifiable behavior hypothesis.
2. Add the smallest Given/When/Then scenario that distinguishes the expected behavior from the suspected defect. Keep real application composition, middleware, antiforgery, model binding, policies, handlers, and serializers active.
3. Replace only external dependencies owned by the scenario. Record exact downstream DTOs and operation ordering in thread-safe queues. Use Playwright routing only when the browser behavior itself needs a controlled response sequence, such as polling races.
4. Cover both UI visibility and forged direct requests for authorization-sensitive actions. Role success alone is insufficient; include direct grants and mismatched game/server scopes where applicable.
5. Start response waits before the triggering click, match the exact HTTP method and route path, and use retrying `Assertions.Expect` checks for asynchronous DOM state. Do not use arbitrary sleeps as the primary synchronization mechanism.
6. Run the focused feature category while iterating. When a scenario exposes a production defect, fix the controlling path and retain the scenario as the regression specification.
7. Before completion, run the complete isolated suite, CI-equivalent unit/build checks, format verification, and the required `code-review` agent.

All `@workflow` scenarios remain serialized. A scenario owns one browser fixture and must not replace it without disposing the previous Kestrel host and browser context.

## Lessons from discovered defects

The current workflows have repeatedly found these defect classes:

- HTTP 200 responses containing `{ success: false }` being displayed as success because JavaScript checked only transport success.
- Buttons rendered on one tab but bound only after a different tab was opened.
- UI authorization flags that did not include every policy enforced by the POST endpoint.
- Role claims checked by type without validating their game-scoped value.
- Client-posted slot, GUID, or display-name values being trusted for RCON targeting and audit records instead of resolving a fresh canonical server/repository identity.
- Successful external side effects followed by failed persistence being reported as complete success or omitted from telemetry.
- Polling modules overlapping requests, losing cursors on empty responses, polling while hidden, retaining stale rows after source changes, or mutating state after disposal.
- Text escaping being reused in HTML attribute contexts where quotes also require encoding.
- Server-originated names or messages reaching Toastr without `escapeHtml` enabled.
- Whole-document settings updates overwriting independently owned runtime state.

Strict browser diagnostics are intentional. Unexpected external requests, same-origin request failures, HTTP errors, console errors, and page errors should be treated as defects until disproved. If cancellation is expected behavior, assert exactly one expected method/path and the browser abort reason rather than adding a broad allowlist. A one-off static asset failure should be reproduced with the focused pack; do not suppress it merely because a rerun passes.

## Known boundaries

- CoD4x lifecycle requests currently use whole-document `UpsertConfiguration`. In-process locking and pending-request rejection prevent duplicate requests within one portal process, but safe cross-process/agent concurrency requires an atomic operation-request endpoint or ETag/conditional write in `portal-repository`. `portal-web` currently consumes Repository packages `4.2.16`; complete the owner change, publish new packages, then update the consumer. Do not bridge this boundary with copied contracts or direct HTTP calls.
- Screenshot configuration is covered by existing parser, serializer, view-model, and controller tests. Runtime screenshot capture, gallery, and delete workflows were skipped because `portal-web` currently has policies but no product endpoints or views for those operations.
- The current validated baseline is 109 isolated UI integration tests and 352 unit tests. The full browser suite remains below the ten-minute budget, but continue measuring runtime as new packs are added.
