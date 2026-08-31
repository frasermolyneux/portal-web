---
applyTo: "src/**/*.cshtml"
---

# Razor and UI guidance

Follow the repository [UI standards guide](../../docs/ui-standards-guide.md).

- Gate resource-sensitive UI with the existing `policy` and `policy-resource`
  tag-helper pattern; use `PotentialAccessProbe.Instance` when asking whether an
  action is possible before a concrete resource exists.
- Use a dedicated confirmation page for primary-entity deletion or the existing
  `data-confirm` pattern for inline destructive actions. Do not add inline
  `onclick` or `onsubmit` confirmation code.
- Do not introduce legacy UI constructs such as `control-label`, `help-block`,
  `float-e-margins`, `btn-xs`, `dl-horizontal`, `admin-actions-filters`,
  deprecated Font Awesome aliases, or `type="button"` on links.
- Validate changed views with a Razor-compiling build.
