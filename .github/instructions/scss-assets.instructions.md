---
applyTo: "src/XtremeIdiots.Portal.Web/Styles/**/*.scss,src/XtremeIdiots.Portal.Web/package.json"
---

# SCSS asset guidance

Follow the repository [CSS architecture guide](../../docs/css-architecture-guide.md).

- Preserve the `tokens`, `base`, `layout`, `utilities`, `components`, `features`,
  and `vendor` layering imported by `Styles/app.scss`.
- Put reusable rules in the appropriate shared layer and page-specific rules in
  `features`; do not edit generated `wwwroot/css/app.css` directly.
- Run `npm install` when dependencies are absent, then `npm run build:css:dev`
  from `src/XtremeIdiots.Portal.Web`. There is no npm lockfile, so do not use
  `npm ci`.
