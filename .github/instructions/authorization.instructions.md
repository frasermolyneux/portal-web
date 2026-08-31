---
applyTo: "src/XtremeIdiots.Portal.Web/Auth/**/*.cs,src/XtremeIdiots.Portal.Web/Extensions/PolicyExtensions.cs,src/XtremeIdiots.Portal.Web/Helpers/PolicyTagHelper.cs,src/XtremeIdiots.Portal.Web.Tests/Auth/**/*.cs"
---

# Authorization guidance

Follow the repository [authorization model](../../docs/authorization-model.md).

- Keep policy names in the `{Domain}.{Action}` convention, define constants in
  `AuthPolicies`, and register requirements and handlers through
  `PolicyExtensions.AddXtremeIdiotsPolicies()`.
- Authorization handlers are the source of truth for role and direct-permission
  evaluation. Do not replace handler checks with direct claim checks.
- Use the concrete scoped resource when it exists. Use
  `PotentialAccessProbe.Instance` only for potential-access checks before a
  resource exists; keep `null` fail-closed.
- Update focused handler tests when policy behavior changes.
