# portal-web Copilot instructions

## Repository

This repository contains the XtremeIdiots Portal web front end for player and
game-server administration. The main application is ASP.NET Core 10 with
server-rendered Razor views, EF Core, Application Insights, Azure App
Configuration, typed API clients, and SCSS compiled with Sass. Azure
infrastructure is defined with Terraform.

`global.json` pins .NET SDK `10.0.400`. Projects target `net10.0`; Terraform requires
version `1.15.6` or later.

## Layout

- `src/XtremeIdiots.Portal.Web/` - application code, views, services, and styles.
- `src/XtremeIdiots.Portal.Web.Tests/` - unit and controller tests.
- `src/XtremeIdiots.Portal.Web.IntegrationTests/` - browser integration tests.
- `src/XtremeIdiots.Portal.Integrations.Forums/` - forum integration.
- `terraform/` - Azure workload infrastructure.
- `docs/` - repository architecture and operational guidance.

## Default commands

For a fresh checkout, run `pwsh -NoProfile -File scripts/setup-test-environment.ps1`.
It validates the SDK from `global.json`, Node.js 22.x from `.node-version`, npm
>=10, and PowerShell >=7.2, installs locked dependencies, builds Release, installs
Chromium and verifies an existing browser smoke test. See
[UI testing](../docs/ui-testing.md). Do not replace this with an npm Playwright
installation; the browser version must match the .NET test package.

```pwsh
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Unit
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite HttpIntegration
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser
dotnet format src/XtremeIdiots.Portal.Web.slnx --verify-no-changes --severity warn
```

Choose the smallest relevant suite and `-Filter`; use `-NoBuild` only for current
outputs in the selected `-Configuration` (Release by default). Unit and HTTP
commands never install or launch browsers. Integration test classes need an
explicit `HttpIntegration` or `Browser` category; browser-backed Reqnroll features
need `@Browser`. Empty and all-skipped selections fail.

Browser ownership is assembly-scoped and lazy with at most two leased contexts.
Keep per-test hosts, data, authentication, and browser storage isolated, and
dispose leases rather than the shared browser. Use retrying Playwright assertions
and explicit request gates, not sleeps, `NetworkIdle`, or blanket test retries.

Use the Actions/PR test summaries and the existing suite artifact to investigate
failures. Traces, screenshots, browser errors, application logs and manifest diffs
are retained under the invocation's `diagnostics/` directory. Do not suppress a
failure or approve a changed action manifest without reviewing that evidence.

Use `-Coverage` with the Unit or HttpIntegration runner for per-suite .NET line
and branch coverage, or `-Measure` for discovery/timing provenance without
instrumentation. Browser coverage is intentionally not collected. Read the scope,
profile/binary hashes, build mode and working-tree flags before comparing results;
do not average overlapping suite coverage or infer flakiness from a single run.
Measurements surface in the existing Actions/PR summary, without percentage gates.

Use targeted validation appropriate to the changed files. Razor compilation can
be checked with `-p:ValidateRazor=true`; SCSS and Terraform commands are documented
in `AGENTS.md` and their focused guides.

## Universal constraints

- Keep nullable reference types and type safety intact; follow established controller,
  service, view-model, and test patterns.
- Do not add credentials or client secrets. Deployed access uses managed identity,
  Azure App Configuration/Key Vault, and GitHub Actions OIDC.
- Treat authorization handlers as the source of truth for access decisions.
- Map persisted settings through `XtremeIdiots.Portal.Settings.Contracts.V1` and
  the shared parser/serializer services rather than controller-local JSON schemas.
- Do not edit deployment workflows or `version.json` unless the task requires it.

## Architecture guidance

- [UI standards](../docs/ui-standards-guide.md)
- [Authorization model](../docs/authorization-model.md)
- [CSS architecture](../docs/css-architecture-guide.md)
- [Platform settings contracts](../docs/platform-settings-contracts.md)
- [DataTables implementation](../docs/DATATABLE-IMPLEMENTATION-GUIDE.md)
- [Development workflows](../docs/development-workflows.md)
