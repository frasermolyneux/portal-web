# XtremeIdiots Portal - Website

[![Build and Test](https://github.com/frasermolyneux/portal-web/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/build-and-test.yml)
[![Code Quality](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml)
[![PR Verify](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml)
[![Deploy Dev](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml)
[![Deploy Prd](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml)
[![Destroy Environment](https://github.com/frasermolyneux/portal-web/actions/workflows/destroy-environment.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/destroy-environment.yml)
[![Copilot Setup Steps](https://github.com/frasermolyneux/portal-web/actions/workflows/copilot-setup-steps.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/copilot-setup-steps.yml)
[![Dependabot Automerge](https://github.com/frasermolyneux/portal-web/actions/workflows/dependabot-automerge.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/dependabot-automerge.yml)

## Documentation

* [Authorization Model](/docs/authorization-model.md) - How roles, policies, and scoping work in the portal.
* [CSS Architecture Guide](/docs/css-architecture-guide.md) - Styling conventions, structure, and tooling for the web UI.
* [Datatable Implementation Guide](/docs/DATATABLE-IMPLEMENTATION-GUIDE.md) - Patterns for server-backed data tables and pagination.
* [Development Workflows](/docs/development-workflows.md) - Branch strategy, CI/CD triggers, and development flows.
* [Identity Manual Run](/docs/identity-manual-run.sql) - Manual SQL to validate or repair identity artifacts.
* [Manual Steps](/docs/manual-steps.md) - Post-deployment configuration steps.
* [Mobile Table Improvements](/docs/mobile-table-improvements.md) - Responsive table patterns and UX notes.

## Overview

Web front end for the XtremeIdiots Portal providing player and game server management for community admins. Built on ASP.NET Core 9 with server-rendered Razor views, shared UI components, and API integrations to enforce bans, manage servers, and surface telemetry. Uses Application Insights, AuthZ/role checks, and the GeoLocation V1.1 intelligence API for IP risk assessment and geolocation. CI/CD runs via GitHub Actions with OIDC deployments to Azure App Service and supporting resources provisioned by Terraform.

## Authorization

The portal uses a structured `{Domain}.{Action}` permissions model. Role claims (SeniorAdmin, HeadAdmin, GameAdmin, Moderator) are synced from forum group membership, while additional permissions can be directly assigned for fine-grained access. All authorization handlers check both paths, so a user's effective permissions are the union of role-based access and any direct grants.

For details on how the authorization model works, see the [Authorization Model](/docs/authorization-model.md) documentation. For exact role-to-permission mappings, the authorization handlers in `src/XtremeIdiots.Portal.Web/Auth/Handlers/` are the source of truth.

## Contributing

Please read the [contributing](CONTRIBUTING.md) guidance; this is a learning and development project.

## Security

Please read the [security](SECURITY.md) guidance; I am always open to security feedback through email or opening an issue.
