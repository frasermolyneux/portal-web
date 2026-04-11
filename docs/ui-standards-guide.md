# UI Standards Guide

This document defines the canonical UI patterns for the XtremeIdiots Portal. All views must follow these standards to ensure visual consistency across the site.

## Page Structure

Every page follows this wrapper structure:

```html
<div class="wrapper wrapper-content animated fadeInRight">
    <div class="row">
        <div class="col-12">
            <div class="container-fluid">
                <div class="ibox">
                    <div class="ibox-title"><h5>Section Title</h5></div>
                    <div class="ibox-content"><!-- Content --></div>
                    <div class="ibox-footer"><!-- Action buttons --></div>
                </div>
            </div>
        </div>
    </div>
</div>
```

### Rules

- Every content section **must** be wrapped in `ibox > ibox-title + ibox-content`.
- The `ibox-title` **must** contain an `<h5>` element. Never place raw text in `ibox-title`.
- Action buttons (Save, Cancel, Delete, Back) go in `ibox-footer`, not loose inside `ibox-content`.
- Use `ibox-footer--sticky` on long forms (e.g. GameServers/Edit) to keep the save bar visible.

---

## Buttons

### Hierarchy

| Role | Class | Icon | Size |
|------|-------|------|------|
| **Primary action** (Save, Create, Submit) | `btn btn-primary` | Optional FA icon + text | Default |
| **Secondary action** (Cancel, Back) | `btn btn-outline-secondary` | No icon, or `fa-arrow-left` for back | Default |
| **Destructive action** (Delete link in table) | `btn btn-outline-danger` | `fa-trash` | `btn-sm` |
| **Destructive confirm** (Delete button on confirmation page) | `btn btn-danger` | `fa-trash` | Default |
| **Cautionary action** (Lift ban) | `btn btn-warning` | `fa-unlock` | Default |
| **Inline table actions** (Edit, Details) | `btn btn-outline-secondary` | Icon + text | `btn-sm` |
| **Filter reset** | `btn btn-outline-secondary btn-sm` | No icon | `btn-sm` |

### Prohibited Button Classes for Actions

Do **not** use these for action buttons (reserve for badges/status only):

- `btn-success` — use `btn-primary` instead
- `btn-info` — use `btn-outline-secondary` instead
- `btn-xs` — use `btn-sm` instead (btn-xs is a bridge alias only)

### Button Placement

- **List pages:** "Create New" button in `.ibox-tools` within `.ibox-title` (top-right).
- **Form pages:** Submit + Cancel in `.ibox-footer`. Primary action on the right, Cancel on the left.
- **Detail pages:** Action buttons in `.ibox-footer`.
- **Table rows:** `btn-group btn-group-sm` in the last column.
- **Confirmation pages:** Cancel (left) + Confirm (right) in `.ibox-footer`.

### Cancel Button Wording

- On **confirmation pages**: use `Cancel` (no icon).
- On **form/edit pages**: use `Back to List` or `Back to [Parent]` with `fa-arrow-left` icon.

---

## Icons

All icons use Font Awesome 6 solid style with fixed-width class: `fa-solid fa-fw fa-[icon-name]`.

### Standard Action Icons

| Action | Icon | Usage |
|--------|------|-------|
| Create / Add | `fa-plus` | Create buttons, add links |
| Edit | `fa-pen-to-square` | Edit buttons and links |
| Delete | `fa-trash` | Delete buttons and links |
| View / Details | `fa-eye` | Details/view buttons |
| Back | `fa-arrow-left` | Back navigation buttons |
| Save | `fa-floppy-disk` | Save/submit buttons |
| Clone | `fa-clone` | Clone/copy actions |
| Import | `fa-file-import` | Import actions |
| Refresh / Sync | `fa-rotate` | Sync/refresh actions |
| Cancel (operation) | `fa-ban` | Cancel running operations |

### Rules

- **Always** include `fa-fw` on icons for fixed-width alignment.
- **Never** use deprecated icons: `fa-save` → `fa-floppy-disk`, `fa-edit` → `fa-pen-to-square`, `fa-file-text` → use `fa-flag` or contextual icon.
- Include `aria-hidden="true"` on decorative icons (those accompanied by text).
- Do not use `me-1` for spacing when `fa-fw` is present — `fa-fw` provides consistent width.

---

## Forms

### Layout

- Use **vertical labels** (label above input) with `form-label` class.
- Wrap each field in `<div class="mb-3">`.
- Never use horizontal form layout (`col-sm-2` + `col-sm-10`).
- Use `form-select` for `<select>` elements, not `form-control`.
- Use `form-text` for helper text beneath inputs, not `help-block`.
- Use `form-check` + `form-check-input` + `form-check-label` for checkboxes.

### Textarea Rows

- Short text fields: `rows="3"`
- Descriptions / content: `rows="6"`

### Long Forms

- Use **Bootstrap nav-tabs** for forms with 5+ logical sections (e.g. GameServers/Edit).
- Use `ibox-footer--sticky` to keep Save/Cancel visible during scrolling.
- Toggle tab visibility with CSS class `d-none`, not inline `style="display:none"`.

### Validation

- Always include `<div asp-validation-summary="ModelOnly" class="text-danger"></div>` at the top of forms.
- Use `<span asp-validation-for="Field" class="text-danger"></span>` below inputs.

---

## Detail Pages

Use the `detail-fields` component for label-value displays:

```html
<dl class="detail-fields row">
    <div class="detail-field col-sm-6">
        <dt class="detail-label">Label</dt>
        <dd class="detail-value">Value</dd>
    </div>
</dl>
```

### Rules

- Always use `<dl>` semantics with `detail-fields` class.
- Use `detail-label` (uppercase, muted, small) and `detail-value` (normal text).
- Arrange fields in a responsive grid: `col-sm-6` (two-column) or `col-sm-4` (three-column).
- Never use `dl-horizontal` (legacy Bootstrap 3 class).

---

## Filter Bars

Use the `list-filters` class for filter bars on list/index pages:

```html
<div class="list-filters mb-2">
    <div class="filter-group">
        <label class="form-label" for="filterGameType">Game Type</label>
        <select id="filterGameType" class="form-select">...</select>
    </div>
    <div class="filter-group">
        <label class="form-label" for="resetFilters">Reset</label>
        <button type="button" id="resetFilters" class="btn btn-outline-secondary btn-sm">Reset Filters</button>
    </div>
</div>
```

### Rules

- Use `list-filters` class, not `admin-actions-filters` (legacy alias retained for backward compatibility).
- Always label the reset button **"Reset Filters"** — not "Clear Filters" or "Reset".
- Place the filter bar **inside** `.ibox-content`, above the table.

---

## Tables & DataTables

### Table Classes

All data tables must use: `table table-striped table-hover`.

### Column Width Utilities

- Date/timestamp columns: add `class="table-date-col"` to `<th>` — applies `white-space: nowrap; width: 1%`.
- Action button columns: add `class="table-action-col"` to `<th>` — same constrained width.

### Empty States

- DataTables: set `language.emptyTable` to a contextual message (e.g. `"No game servers found"`). A global fallback of `"No records found"` is set in `site.js`.
- Non-table contexts: use the `.empty-state` component:

```html
<div class="empty-state">
    <i class="fa-solid fa-fw fa-folder-open empty-state-icon"></i>
    <p class="empty-state-message">No records found</p>
    <div class="empty-state-action">
        <a href="..." class="btn btn-primary btn-sm">Create New</a>
    </div>
</div>
```

---

## Destructive Operations

All destructive actions must have a confirmation gate. Use one of two tiers:

### Tier 1: Dedicated Confirmation Page

For permanent, irreversible deletes of primary entities (game servers, admin actions, ban file monitors, demos, tags, map rotations).

**Standard structure:**

```html
<div class="ibox">
    <div class="ibox-title"><h5>Delete [Entity Type]</h5></div>
    <div class="ibox-content">
        <p>Are you sure you want to delete this [entity type]?</p>
        <div class="alert alert-warning mb-3">
            <i class="fa-solid fa-fw fa-triangle-exclamation" aria-hidden="true"></i>
            This action cannot be undone. [Specific consequence describing sub-entity impact.]
        </div>
        <dl class="detail-fields row">
            <!-- Entity details shown here -->
        </dl>
    </div>
    <div class="ibox-footer">
        <a class="btn btn-outline-secondary" asp-action="Index">Cancel</a>
        <button type="submit" class="btn btn-danger">
            <i class="fa-solid fa-fw fa-trash"></i> Delete [Entity Type]
        </button>
    </div>
</div>
```

### Tier 2: Inline `data-confirm` Dialog

For quick destructive actions within list/detail views (unassign, remove tag, delete map from host, delete permission).

Add a `data-confirm` attribute to the button or link:

```html
<button type="submit" class="btn btn-outline-danger btn-sm"
    data-confirm="Are you sure you want to [verb] this [entity]? [Consequence if applicable.]">
    <i class="fa-solid fa-fw fa-trash"></i> Delete
</button>
```

The `data-confirm` handler in `enhanced-ui.js` intercepts the click and shows a browser confirm dialog. No inline `onclick` or `onsubmit` handlers.

### Confirm Message Format

All confirmation messages follow this pattern:

> Are you sure you want to **[verb]** this **[entity]**? **[Consequence if applicable.]**

Examples:
- "Are you sure you want to delete this protected name?"
- "Are you sure you want to unassign this server? Maps will be removed from the server."
- "Are you sure you want to remove this permission? It may take up to 15 minutes to take effect."

### What NOT to Do

- ❌ `onclick="return confirm('...')"` — use `data-confirm` instead.
- ❌ `onsubmit="return confirm('...')"` — use `data-confirm` on the submit button instead.
- ❌ Direct POST with no confirmation on destructive actions.
- ❌ `btn-danger` on non-destructive actions (Claim, Lift) — use `btn-primary` or `btn-warning`.

---

## Badges & Status Indicators

- Use SCSS-defined `badge-*` classes or Bootstrap 5 `bg-*` classes.
- Colour semantics: green = active/online/success, red = error/expired/danger, amber = warning/pending, teal = primary.
- Use square badges (border-radius: 3px), not pill badges.
- All status values must be wrapped in a badge — never raw text.

---

## Typography

- Font: Open Sans throughout (loaded via Google Fonts).
- ibox-title `<h5>`: renders at 16px (`$h3-size`) with `font-weight: 600`.
- Dashboard stat numbers: use `stat-value` class (renders at `$stat-value-size: 2rem`).
- Never hardcode `px` values in views — use token variables in SCSS.

---

## Legacy Patterns to Avoid

These patterns are deprecated. The SCSS retains bridge definitions for backward compatibility, but new code must not use them:

| Deprecated | Replacement |
|-----------|-------------|
| `control-label` | `form-label` |
| `help-block` | `form-text` |
| `float-e-margins` | Remove — no replacement needed |
| `btn-xs` | `btn-sm` |
| `dl-horizontal` | `detail-fields` with `detail-field` / `detail-label` / `detail-value` |
| `admin-actions-filters` | `list-filters` |
| `form-control` on `<select>` | `form-select` |
| `fa-save` | `fa-floppy-disk` |
| `fa-edit` | `fa-pen-to-square` |
| `type="button"` on `<a>` | Remove — not valid HTML on anchors |
