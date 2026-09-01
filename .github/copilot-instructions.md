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

```pwsh
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj
dotnet test src --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/XtremeIdiots.Portal.Web.slnx --verify-no-changes --severity warn
```

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
