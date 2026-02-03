# CSS Architecture

This document describes the modernized CSS architecture for the XtremeIdiots Portal.

## Overview

The portal uses a structured SCSS-based architecture that compiles to a single `app.css` file. This replaces the previous approach of using multiple CSS files (inspinia.css, site.css, responsive-overrides.css, etc.).

## Structure

```
Styles/
├── tokens/              # Design system variables
│   ├── _colors.scss     # Color palette and CSS custom properties
│   ├── _spacing.scss    # Spacing scale and component spacing
│   └── _typography.scss # Font families, sizes, weights
├── base/                # Foundation styles
│   ├── _reset.scss      # CSS reset
│   ├── _base.scss       # HTML element styles
│   └── _accessibility.scss # Accessibility improvements
├── layout/              # Page structure
│   ├── _responsive.scss # Breakpoints and responsive mixins
│   ├── _navbar.scss     # Top navigation bar
│   ├── _sidebar.scss    # Off-canvas sidebar
│   ├── _footer.scss     # Page footer
│   └── _wrapper.scss    # Main content wrapper
├── utilities/           # Helper classes
│   ├── _spacing.scss    # Margin and padding utilities
│   ├── _flex.scss       # Flexbox utilities
│   ├── _text.scss       # Text alignment, colors, sizing
│   └── _zindex.scss     # Z-index layering utilities
├── components/          # Reusable UI components
│   ├── _buttons.scss    # Button styles
│   ├── _cards.scss      # ibox card component
│   ├── _tables.scss     # Table styles
│   ├── _forms.scss      # Form controls
│   ├── _badges.scss     # Badge components
│   └── _pagination.scss # Pagination controls
├── features/            # Page-specific styles
│   ├── _admin-actions.scss # Admin actions pages
│   ├── _players.scss    # Players pages
│   ├── _chatlog.scss    # Chat log pages
│   ├── _maps.scss       # Maps pages
│   ├── _demos.scss      # Demos pages
│   └── _users.scss      # User management pages
├── vendor/              # Third-party overrides
│   ├── _bootstrap-overrides.scss  # Bootstrap customizations
│   └── _datatable-overrides.scss  # DataTables customizations
└── app.scss             # Main entry point (imports all partials)
```

## Build Process

### Development

The SCSS files are automatically compiled during `dotnet build`:

```bash
dotnet build                    # Compiles SCSS with source maps
```

For development with watch mode:

```bash
npm run watch:css              # Watches for SCSS changes
```

### Production

Production builds compile SCSS without source maps:

```bash
dotnet build --configuration Release
```

### Manual Compilation

You can also compile SCSS manually:

```bash
npm run build:css              # Production (no source maps)
npm run build:css:dev          # Development (with source maps)
```

## Output

All SCSS compiles to:
- `wwwroot/css/app.css` - Single compiled CSS file
- `wwwroot/css/app.css.map` - Source map (development only)

## Layering Strategy

The SCSS is imported in a specific order to ensure proper CSS cascade:

1. **Tokens** - Variables and CSS custom properties
2. **Base** - HTML element defaults
3. **Layout** - Page structure and navigation
4. **Utilities** - Helper classes
5. **Components** - Reusable UI components
6. **Features** - Page-specific styles
7. **Vendor** - Third-party library overrides

## Key Features

### Design Tokens

Variables are centralized in the `tokens/` directory:

```scss
// From tokens/_colors.scss
$xi-primary: #1ab394;
$xi-secondary: #293846;

// From tokens/_spacing.scss
$spacing-md: 1rem;
$ibox-margin: 25px;

// From tokens/_typography.scss
$font-family-base: "Open Sans", sans-serif;
$font-weight-semibold: 600;
```

### Responsive Mixins

Use semantic breakpoint mixins from `layout/_responsive.scss`:

```scss
@include media-md {
    // Styles for >= 768px
}

@include media-max-md {
    // Styles for < 768px
}
```

### Component-Based Organization

Components like the ibox card are self-contained:

```scss
// From components/_cards.scss
.ibox {
    margin-bottom: $ibox-margin;
    background-color: $white-bg;
    border: 1px solid $border-color;
    // ... more styles
}
```

### Feature Isolation

Page-specific styles are isolated in `features/`:

```scss
// From features/_admin-actions.scss
.admin-action-card {
    transition: all 0.3s ease;
    // ... admin-specific styles
}
```

## ASP.NET Integration

The compiled CSS is referenced in `Views/Shared/_Layout.cshtml`:

```html
<environment names="Development">
    <link rel="stylesheet" href="~/css/app.css" asp-append-version="true" />
</environment>
<environment names="Staging,Production">
    <link rel="stylesheet" href="~/css/app.css" asp-append-version="true" />
</environment>
```

The `asp-append-version` tag helper ensures proper cache busting when CSS changes.

## Vendor Dependencies

Third-party CSS libraries are loaded separately and only overridden in `vendor/` partials:

**Included as external files:**
- Bootstrap 5.3
- DataTables
- Font Awesome
- Toastr

**Overridden in SCSS:**
- `vendor/_bootstrap-overrides.scss` - Bootstrap customizations
- `vendor/_datatable-overrides.scss` - DataTables styling

## Migration from Inspinia

The previous Inspinia theme (inspinia.css) has been absorbed into the structured SCSS:

- **Layout styles** → `layout/_navbar.scss`, `layout/_sidebar.scss`
- **ibox components** → `components/_cards.scss`
- **Navigation** → `layout/_sidebar.scss`
- **Utilities** → `utilities/` directory

## Adding New Styles

### For a new page:

1. Create a new file in `features/`: `Styles/features/_mypage.scss`
2. Add feature-specific styles
3. Import in `Styles/app.scss`: `@use 'features/mypage';`
4. Rebuild: `dotnet build`

### For a new component:

1. Create a new file in `components/`: `Styles/components/_mycomponent.scss`
2. Add reusable component styles
3. Import in `Styles/app.scss`: `@use 'components/mycomponent';`
4. Rebuild: `dotnet build`

### For theme changes:

1. Edit variables in `tokens/_colors.scss` or other token files
2. The changes cascade throughout the application
3. Rebuild: `dotnet build`

## Best Practices

1. **Use variables** - Always use tokens variables instead of hardcoded values
2. **Use mixins** - Leverage responsive mixins for breakpoints
3. **Scope features** - Keep page-specific styles in `features/`
4. **Component reuse** - Extract reusable patterns into `components/`
5. **Avoid !important** - Use proper specificity instead
6. **Mobile-first** - Design for mobile first, enhance for desktop
7. **Test responsive** - Always test changes across breakpoints

## Troubleshooting

### SCSS not compiling

If SCSS doesn't compile on build:

```bash
# Install/reinstall npm dependencies
npm install

# Manually compile to check for errors
npm run build:css:dev
```

### Styles not updating

If changes aren't reflected:

1. Clear browser cache (Ctrl+F5)
2. Rebuild the project: `dotnet clean && dotnet build`
3. Check that `app.css` timestamp is current

### Deprecation warnings

SCSS compilation may show informational warnings that don't affect functionality. These are kept up-to-date with the latest Sass best practices.

## Best Practices
