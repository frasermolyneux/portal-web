# AGENTS.md — portal-web

ASP.NET Core 9 web application — the XtremeIdiots Portal front-end. Razor views (runtime compile in Debug, build-time precompile in Release), SCSS via npm, Application Insights, Azure App Configuration, EF Core for identity / data-protection, and typed API clients for the Portal Repository, Servers Integration, and GeoLocation APIs.

This file is the brief for the **GitHub Copilot coding agent** (and any other agent that follows the [agents.md](https://agents.md) convention) when it runs in a cloud runner without the local VS Code multi-root workspace context.

> If you are a human reading this in VS Code, prefer `.github/copilot-instructions.md` for project orientation. `AGENTS.md` is the agent execution brief.

---

## Required reading (read these BEFORE doing any work)

The `copilot-setup-steps.yml` workflow checks out `frasermolyneux/.github-copilot` at `./.github-copilot/` in the runner, so the paths below resolve.

1. `.github/copilot-instructions.md` — repo-specific orientation, build commands, conventions
2. `.github-copilot/.github/instructions/personal.working-preferences.instructions.md`
3. `.github-copilot/.github/copilot-instructions.md` — org-wide catalog
4. Stack-specific files — see **Stack guardrails** below
5. `docs/ui-standards-guide.md` — **mandatory for any Razor view change** (buttons, icons, forms, destructive operations, legacy patterns to avoid)
6. `docs/authorization-model.md` — how roles, policies, and `PotentialAccessProbe` work
7. `docs/css-architecture-guide.md` — SCSS structure, tokens, components
8. `docs/DATATABLE-IMPLEMENTATION-GUIDE.md` — server-backed data table patterns

---

## Stack guardrails

### Tenant facts (always-on)
- `tenant.subscriptions`, `tenant.regions`, `tenant.identity`, `tenant.dns`

### Enforceable standards
- `standards.oidc-and-secrets` — **no client secrets**
- `standards.dotnet-project`
- `standards.azure-naming`, `standards.azure-tagging`, `standards.terraform-style`
- `standards.branching-and-prs`

### Patterns
- `patterns.api-client` — consumes Portal Repository + Servers Integration + GeoLocation clients
- `patterns.scss-build` — SCSS build pattern
- `patterns.nbgv-versioning`
- `patterns.terraform-remote-state`

### Platform settings contracts
- Use `XtremeIdiots.Portal.Settings.Contracts.V1` for typed settings namespace mapping/validation.
- Keep settings parsing/serialization in `Services/Settings`; avoid introducing controller-level raw namespace JSON parsing.
- Do not reintroduce settings dependencies on `XtremeIdiots.Portal.ChatCommands.Abstractions.V1`; that path is compatibility-only.
- Do not remove compatibility shims unless cross-repo shim-removal gate evidence is recorded.

### Platform consumption contracts
- `platform.workloads`, `platform.monitoring`, `platform.hosting`, `platform.connectivity`

### Shared
- `shared.api-client-abstractions`
- `shared.observability-appinsights`
- `shared.portal-core` — App Insights / ASP / SQL consumed from `portal-core`

---

## Build, test, format

```pwsh
# .NET
dotnet build src/XtremeIdiots.Portal.Web/XtremeIdiots.Portal.Web.csproj
dotnet test src --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/XtremeIdiots.Portal.Web.sln --verify-no-changes

# SCSS (npm install runs automatically on first build via MSBuild target)
cd src/XtremeIdiots.Portal.Web
npm install
npm run build:css:dev
cd ../..

# Terraform
terraform -chdir=terraform fmt -check -recursive
terraform -chdir=terraform init -backend-config=backends/dev.backend.hcl
terraform -chdir=terraform validate
terraform -chdir=terraform plan -var-file=tfvars/dev.tfvars
```

Release builds treat warnings as errors and precompile Razor views — check `ValidateRazor=true` Razor compilation succeeds before declaring done.

---

## Do NOT

- ❌ Do not `git commit`, `git push`, force-push, rebase, or branch-mutate. Work on the assigned branch only.
- ❌ Do not introduce client secrets. App Configuration + Key Vault + managed identity only.
- ❌ Do not bypass `dotnet format`, `dotnet test`, `terraform fmt`, `terraform validate`, or the SCSS build.
- ❌ Do not use legacy Razor / Bootstrap classes: `control-label`, `help-block`, `float-e-margins`, `btn-xs`, `dl-horizontal`, `admin-actions-filters`, `fa-save`, `fa-edit`, `type="button"` on `<a>` tags. See `docs/ui-standards-guide.md`.
- ❌ Do not use inline `onclick` / `onsubmit` confirm handlers — use Tier 1 (confirmation page) or Tier 2 (`data-confirm` attribute) per the UI standards guide.
- ❌ Do not use `ClaimedGamesAndItems` or direct claim checks as authorization gates — use `PotentialAccessProbe` instead. The handler is the single source of truth.
- ❌ Do not bypass the `{Domain}.{Action}` policy convention — register new policies via `PolicyExtensions.AddXtremeIdiotsPolicies()`.
- ❌ Do not modify `.github/workflows/`, `.github/dependabot.yml`, or `version.json` unless that is the explicit task.

---

## Opening the PR

You MUST use `.github/PULL_REQUEST_TEMPLATE.md` as your PR body — do **not** write a freeform body. The org template is inherited from `frasermolyneux/.github` and GitHub pre-populates it when you open the PR. Concretely:

1. Fill `## Summary` (one line) and `Closes #<issue>`.
2. Tick the relevant `## Type of change` box.
3. Paste the **actual command output** from your Build, Tests, and Format check runs into `## Validation evidence`. Show the real summary line, not "tests passed".
4. Fill `## Risk and rollout` — blast radius, auto-deploy?, manual steps post-merge, rollback plan.
5. Tick **every** box in `## Agent attestation`.
6. Delete `## Consumer impact` only if no published contract (Abstractions / Client NuGet / Service Bus DTO / Terraform output) changed.

Complete the `## Agent attestation` section before requesting review; reviewers use it as a readiness checklist.

---

## Pre-PR checks (run before you open the PR)

- [ ] `dotnet build` succeeds (clean) — Release config catches warning-as-error and Razor precompile issues
- [ ] `dotnet test --filter "FullyQualifiedName!~IntegrationTests"` passes
- [ ] `dotnet format --verify-no-changes` passes
- [ ] `npm run build:css:dev` succeeds
- [ ] `terraform fmt -check -recursive` passes
- [ ] `terraform validate` + `terraform plan -var-file=tfvars/dev.tfvars` succeed
- [ ] All Razor view changes follow `docs/ui-standards-guide.md` (buttons, icons, forms, destructive gating)
- [ ] New authorization checks use `PotentialAccessProbe` for "can user potentially..." gates
- [ ] No new secrets / GUIDs / connection strings
- [ ] PR body cites each acceptance criterion
- [ ] Risk/rollout section filled in

---

## Escalation

If you hit any of the conditions below, **open the PR as draft** and **apply the `needs-decision` label** instead of pushing forward to ready-for-review. Post a comment on the originating issue summarising what's blocking you and what decision is needed.

Stop and escalate when:

- The change requires modifying `AdditionalPermission` constants in the `portal-repository` abstractions package (coordinate there first).
- A new authorization policy crosses domain boundaries and would need cross-repo coordination.
- A `code-review` finding is **High** and cannot be resolved in-scope.
- The SCSS build fails and `npm install` + `npm run build:css:dev` does not resolve it.
