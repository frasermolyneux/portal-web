# XtremeIdiots Portal - Website

[![Code Quality](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/codequality.yml)
[![PR Verify](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/pr-verify.yml)
[![Deploy Dev](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-dev.yml)
[![Deploy PRD](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml/badge.svg)](https://github.com/frasermolyneux/portal-web/actions/workflows/deploy-prd.yml)

## Documentation

* [Development Workflows](/docs/development-workflows.md) - Branch strategy, CI/CD triggers, and development flows
* [Manual Steps](/docs/manual-steps.md) - Post-deployment configuration steps

---

## Overview

This repository contains XtremeIdiots Portal solution that provides player and game server management for the XtremeIdiots community. There are several integrations with the game servers to collect player data and services to enforce player bans.

The primary users for the website are the community admins that perform the game server and player management.

---

## Related Projects

* [frasermolyneux/azure-landing-zones](https://github.com/frasermolyneux/azure-landing-zones) - The deploy service principal is managed by this project, as is the workload subscription.
* [frasermolyneux/platform-connectivity](https://github.com/frasermolyneux/platform-connectivity) - The platform connectivity project provides DNS and Azure Front Door shared resources.
* [frasermolyneux/platform-strategic-services](https://github.com/frasermolyneux/platform-strategic-services) - The platform strategic services project provides a shared services such as API Management and App Service Plans.

---

## Solution

TODO

---

## Azure Pipelines

The `one-pipeline` is within the `.azure-pipelines` folder and output is visible on the [frasermolyneux/Personal-Public](https://dev.azure.com/frasermolyneux/XtremeIdiots-Public/_build?definitionId=177) Azure DevOps project.
The `.github` folder contains `dependabot` configuration and some code quality workflows.

---

## Contributing

Please read the [contributing](CONTRIBUTING.md) guidance; this is a learning and development project.

---

## Security

Please read the [security](SECURITY.md) guidance; I am always open to security feedback through email or opening an issue.
