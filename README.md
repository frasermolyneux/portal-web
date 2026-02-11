# XtremeIdiots Portal - Website
[![Build and Test](https://github.com/frasermolyneux/portal-web/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/build-and-test.yml)
[![Code Quality](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml)
[![PR Verify](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml)
[![Deploy Dev](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml)
[![Deploy Prd](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml)
[![Copilot Setup Steps](https://github.com/frasermolyneux/portal-web/actions/workflows/copilot-setup-steps.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/copilot-setup-steps.yml)
[![Dependabot Automerge](https://github.com/frasermolyneux/portal-web/actions/workflows/dependabot-automerge.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/dependabot-automerge.yml)

## Documentation
* [Credentials Permissions Matrix](/docs/credentials-permissions-matrix.md) - Access and permission mapping for portal services and client roles.
* [CSS Architecture Guide](/docs/css-architecture-guide.md) - Styling conventions, structure, and tooling for the web UI.
* [Datatable Implementation Guide](/docs/DATATABLE-IMPLEMENTATION-GUIDE.md) - Patterns for server-backed data tables and pagination.
* [Development Workflows](/docs/development-workflows.md) - Branch strategy, CI/CD triggers, and development flows.
* [Identity Manual Run](/docs/identity-manual-run.sql) - Manual SQL to validate or repair identity artifacts.
* [Manual Steps](/docs/manual-steps.md) - Post-deployment configuration steps.
* [Mobile Table Improvements](/docs/mobile-table-improvements.md) - Responsive table patterns and UX notes.
* [ProxyCheck Integration](/docs/proxycheck-integration.md) - Anti-proxy integration notes and configuration.

## Overview
Web front end for the XtremeIdiots Portal providing player and game server management for community admins. Built on ASP.NET Core with server-rendered views, shared UI components, and API integrations to enforce bans, manage servers, and surface telemetry. Uses Application Insights, AuthZ/role checks, and defensive anti-proxy validation to protect sessions. CI/CD runs via GitHub Actions with OIDC deployments to Azure App Service and supporting resources provisioned by Terraform.

## Contributing
Please read the [contributing](CONTRIBUTING.md) guidance; this is a learning and development project.

## Security
Please read the [security](SECURITY.md) guidance; I am always open to security feedback through email or opening an issue.
