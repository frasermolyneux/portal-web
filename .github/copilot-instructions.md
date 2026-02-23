# Copilot Instructions

## Project Overview

ASP.NET Core 9 web application (`src/XtremeIdiots.Portal.Web/`) providing the XtremeIdiots Portal front end for player and game server management. Uses Razor views with runtime compilation in Debug and build-time compilation in Release, Application Insights for telemetry, Azure App Configuration, Entity Framework Core for identity/data-protection, and API clients for the Repository API, Servers Integration API, and GeoLocation API.

## Build, Test, and Run

- **Solution**: `src/XtremeIdiots.Portal.Web.sln`
- **Build**: `dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj`
- **Test**: `dotnet test src --filter "FullyQualifiedName!~IntegrationTests"`
- **Clean**: `dotnet clean src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj`
- Release builds treat warnings as errors and precompile Razor views.
- Integration tests are filtered out by default; add new tests to `src/XtremeIdiots.Portal.Web.Tests/`.

## Front-End Assets (SCSS)

- SCSS source lives under `src/XtremeIdiots.Portal.Web/Styles/`; see [docs/css-architecture-guide.md](docs/css-architecture-guide.md).
- `npm install` runs automatically on first build via MSBuild targets.
- Debug builds compile with `npm run build:css:dev`; Release uses `npm run build:css`.
- For live editing run `npm run watch:css` from `src/XtremeIdiots.Portal.Web/`.
- If SCSS fails, run `npm install` then `npm run build:css:dev` to reset.

## Project Structure

- `src/XtremeIdiots.Portal.Web/` — Main web application (Controllers, Views, ViewComponents, ApiControllers, Models, Services, Styles).
- `src/XtremeIdiots.Portal.Integrations.Forums/` — Forum integration library.
- `src/XtremeIdiots.Portal.Web.Tests/` — Unit tests.
- `terraform/` — Infrastructure as code for Azure deployments.
- `.github/workflows/` — CI/CD pipelines (build-and-test, pr-verify, deploy-dev, deploy-prd, codequality, destroy-environment, copilot-setup-steps, dependabot-automerge).
- `docs/` — Architecture guides, workflow docs, and operational runbooks.

## Key Documentation

- [Development Workflows](docs/development-workflows.md) — Branch strategy, CI/CD triggers, and PR flows.
- [Datatable Implementation Guide](docs/DATATABLE-IMPLEMENTATION-GUIDE.md) — Server-backed data table patterns.
- [Credentials Permissions Matrix](docs/credentials-permissions-matrix.md) — Roles, claims, and access mapping.
- [Permissions Matrices](docs/permissions/) — Per-area authorization matrices (players, game servers, admin actions, etc.).
- [CSS Architecture Guide](docs/css-architecture-guide.md) — Styling conventions and structure.
- [Manual Steps](docs/manual-steps.md) — Post-deployment configuration.

## Conventions and Patterns

- Use nullable reference types (`<Nullable>enable</Nullable>`) and implicit usings.
- Follow existing controller/view/service patterns when adding new features.
- Razor view validation: set `ValidateRazor=true` on build to catch compilation errors early.
- Sensitive settings use user secrets locally (`UserSecretsId` in csproj) or environment variables; Azure App Configuration and managed identity in deployed environments.
- PRs trigger dev Terraform plans automatically; prod plans require the `run-prd-plan` label.
- Copilot and Dependabot PRs skip Terraform plans unless explicitly labeled.
