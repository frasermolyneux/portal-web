# Copilot Instructions

## Project Overview

ASP.NET Core 9 web application (`src/XtremeIdiots.Portal.Web/`) providing the XtremeIdiots Portal front end for player and game server management. Uses Razor views with runtime compilation in Debug and build-time compilation in Release, Application Insights for telemetry, Azure App Configuration, Entity Framework Core for identity/data-protection, and API clients for the Repository API, Servers Integration API, and GeoLocation API. IP intelligence (geolocation + risk assessment) is provided by the GeoLocation V1.1 intelligence endpoint (`geoLocationClient.GeoLookup.V1_1.GetIpIntelligence()`) which returns `IpIntelligenceDto` combining MaxMind geo data with ProxyCheck risk data in a single call.

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
- [UI Standards Guide](docs/ui-standards-guide.md) — Button hierarchy, icons, forms, detail pages, filters, destructive operation gating, and legacy patterns to avoid.
- [Datatable Implementation Guide](docs/DATATABLE-IMPLEMENTATION-GUIDE.md) — Server-backed data table patterns.
- [Credentials Permissions Matrix](docs/credentials-permissions-matrix.md) — Roles, claims, and access mapping.
- [Permissions Matrices](docs/permissions/) — Per-area authorization matrices (players, game servers, admin actions, etc.).
- [CSS Architecture Guide](docs/css-architecture-guide.md) — SCSS structure, tokens, components, and build process.
- [Manual Steps](docs/manual-steps.md) — Post-deployment configuration.

## Conventions and Patterns

- Use nullable reference types (`<Nullable>enable</Nullable>`) and implicit usings.
- Follow existing controller/view/service patterns when adding new features.
- Razor view validation: set `ValidateRazor=true` on build to catch compilation errors early.
- Sensitive settings use user secrets locally (`UserSecretsId` in csproj) or environment variables; Azure App Configuration and managed identity in deployed environments.
- PRs trigger dev Terraform plans automatically; prod plans require the `run-prd-plan` label.
- Copilot and Dependabot PRs skip Terraform plans unless explicitly labeled.

## UI Conventions (Razor Views)

**Always read [docs/ui-standards-guide.md](docs/ui-standards-guide.md) before creating or modifying views.** Key rules:

- Wrap all content in `ibox > ibox-title (with h5) + ibox-content`. Action buttons go in `ibox-footer`.
- Buttons: `btn-primary` for primary actions, `btn-outline-secondary` for cancel/back, `btn-danger` for destructive confirms, `btn-outline-danger btn-sm` for inline delete links.
- Icons: always use `fa-solid fa-fw fa-[icon]` with `aria-hidden="true"`. Use `fa-pen-to-square` (edit), `fa-eye` (details), `fa-trash` (delete), `fa-plus` (create), `fa-floppy-disk` (save), `fa-arrow-left` (back).
- Forms: vertical labels (`form-label`), `form-select` for dropdowns, `form-text` for help text, `mb-3` spacing.
- Detail pages: use `detail-fields` component with `detail-label` / `detail-value` in a `row` grid.
- Filters: use `list-filters` class, label reset button "Reset Filters".
- Destructive actions: Tier 1 (confirmation page) for entity deletes, Tier 2 (`data-confirm` attribute) for inline actions. Never use inline `onclick`/`onsubmit` confirm handlers.
- Tables: `table table-striped table-hover`, `table-date-col` on date headers, `table-action-col` on action headers.
- Do not use legacy patterns: `control-label`, `help-block`, `float-e-margins`, `btn-xs`, `dl-horizontal`, `admin-actions-filters`, `fa-save`, `fa-edit`, `type="button"` on `<a>` tags.
