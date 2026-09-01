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

The required SDK is pinned by `global.json` to .NET SDK `10.0.400`. NuGet packages
come from nuget.org. The web project runs `npm install` automatically when
`node_modules` is absent and compiles SCSS during `dotnet build`.

```pwsh
dotnet restore src/XtremeIdiots.Portal.Web.slnx
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj
dotnet test src --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/XtremeIdiots.Portal.Web.slnx --verify-no-changes --severity warn
```

For Razor changes, compile views explicitly:

```pwsh
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj -p:ValidateRazor=true
```

For SCSS-only work, run from `src/XtremeIdiots.Portal.Web`:

```pwsh
npm install
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
