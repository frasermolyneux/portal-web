# portal-web agent brief

`portal-web` is the ASP.NET Core 10 front end for the XtremeIdiots Portal. It uses
server-rendered Razor views, SCSS compiled with Sass, Entity Framework Core,
Application Insights, Azure App Configuration, typed portal API clients, and
Terraform for Azure infrastructure.

## Repository map

- `src/XtremeIdiots.Portal.Web/` - web application, Razor views, services, and SCSS.
- `src/XtremeIdiots.Portal.Web.Tests/` - unit and controller tests.
- `src/XtremeIdiots.Portal.Web.IntegrationTests/` - Playwright/Reqnroll integration tests.
- `src/XtremeIdiots.Portal.Integrations.Forums/` - forum integration library.
- `terraform/` - workload infrastructure and environment configuration.
- `docs/` - architecture, UI, authorization, settings, and operational guidance.

## Bootstrap and validation

The required SDK is pinned by `global.json` to .NET SDK `10.0.400`. Test setup also
requires Node.js 22.x (`.node-version`), npm >=10, and PowerShell >=7.2. NuGet
packages come from nuget.org. The web project runs `npm ci` when its locked
dependencies need installing and compiles SCSS during `dotnet build`.

Prepare a fresh checkout with the same bootstrap used by CI, devcontainers, and
Copilot setup:

```pwsh
pwsh -NoProfile -File scripts/setup-test-environment.ps1
```

This installs locked frontend dependencies, builds the solution in Release,
installs matching Chromium (including Linux system dependencies), and runs one
existing browser smoke test. It needs no Azure credentials or external services.
See [UI testing](docs/ui-testing.md) for prerequisites and troubleshooting.

```pwsh
dotnet restore src/XtremeIdiots.Portal.Web.slnx
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Unit
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite HttpIntegration
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser
dotnet format src/XtremeIdiots.Portal.Web.slnx --verify-no-changes --severity warn
```

Unit and HTTP execution do not install or launch Chromium. Use `-Filter` for a
focused test/category and `-Configuration Debug` when checking runtime-compiled
Razor. Only use `-NoBuild` when the selected configuration's outputs are current.
The runner rejects zero-test and all-skipped selections. Integration tests must
declare an explicit `HttpIntegration` or `Browser` category; browser-backed
Reqnroll features require `@Browser`.

Failure evidence is stored with the suite TRX under
`src/TestResults/<Suite>/<run-id>/diagnostics/`. Check the Actions test summary,
failure annotations, and linked suite artifact before rerunning a failure.
Keep synthetic credentials in tests: browser traces may include rendered data.
See [UI testing diagnostics](docs/ui-testing.md#diagnosing-failures).

For Razor changes, compile views explicitly:

```pwsh
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj -p:ValidateRazor=true
```

For SCSS-only work, run from `src/XtremeIdiots.Portal.Web`:

```pwsh
npm ci
npm run build:css:dev
```

For Terraform-only work, start with:

```pwsh
terraform -chdir=terraform fmt -check -recursive
```

Terraform initialization, validation, and plans require the environment-specific
backend and Azure OIDC context used by the repository workflows.

## Material risks

- Release builds precompile Razor views; Debug builds normally use runtime compilation.
- The .NET build invokes npm and can modify generated CSS under `wwwroot/css`.
- Authorization is resource-scoped; handlers are authoritative and missing resources fail closed.
- Settings JSON is persisted dynamically, but runtime mapping must use the typed settings contracts.
- Terraform consumes several platform remote states and must continue to use OIDC rather than secrets.

Use the focused guidance in:

- [UI standards](docs/ui-standards-guide.md)
- [Authorization model](docs/authorization-model.md)
- [CSS architecture](docs/css-architecture-guide.md)
- [Platform settings contracts](docs/platform-settings-contracts.md)
- [DataTables implementation](docs/DATATABLE-IMPLEMENTATION-GUIDE.md)
- [Development workflows](docs/development-workflows.md)
