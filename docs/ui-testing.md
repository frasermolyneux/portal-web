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
- `Health/` verifies required liveness, readiness, and version endpoints.
- `Playwright/` verifies rendered pages, browser behavior, policy-controlled UI, and direct authorization enforcement.

Browser tests reject unexpected external requests and fail on same-origin request failures, HTTP error responses, console errors, and page errors. Known cosmetic CDN styles are omitted in the isolated environment.

## Adding coverage

Use HTTP integration tests for broad routing, endpoint, and Razor rendering coverage. Use Playwright when the behavior depends on browser rendering, JavaScript, navigation visibility, or a complete user workflow.

Prefer accessible selectors by role, label, and visible text. Add `data-testid` only when the control has no stable accessible selector. Razor changes must follow `docs/ui-standards-guide.md`.

Authorization tests must keep real policies and handlers active. Add test identities or scenario data through the integration project rather than adding production test-login endpoints or credentials.
