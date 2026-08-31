---
applyTo: "src/XtremeIdiots.Portal.Web/Services/Settings/**/*.cs,src/XtremeIdiots.Portal.Web/Services/GameServerConfigHelper.cs,src/XtremeIdiots.Portal.Web/Controllers/GameServersController.cs,src/XtremeIdiots.Portal.Web/Controllers/GlobalSettingsController.cs,src/XtremeIdiots.Portal.Web/Controllers/Cod4xPluginSettingsJsonHelper.cs,src/XtremeIdiots.Portal.Web/ViewModels/*Settings*.cs,src/XtremeIdiots.Portal.Web.Tests/Services/Settings/**/*.cs,src/XtremeIdiots.Portal.Web.Tests/Controllers/GameServersControllerTests.cs,src/XtremeIdiots.Portal.Web.Tests/Controllers/GlobalSettingsControllerTests.cs,src/XtremeIdiots.Portal.Web.Tests/ViewModels/*Settings*.cs"
---

# Platform settings guidance

Follow the repository [platform settings contracts](../../docs/platform-settings-contracts.md).

- Use contracts and validators from `XtremeIdiots.Portal.Settings.Contracts.V1`.
  Keep namespace mapping in `Services/Settings`; do not add controller-local
  namespace schemas or ad hoc runtime JSON parsing.
- Do not reintroduce `XtremeIdiots.Portal.ChatCommands.Abstractions.V1` as the
  canonical settings contract source.
- For persisted boolean fields, accept legacy string booleans when reading but
  serialize canonical JSON booleans when writing.
- Cover canonical and legacy payload behavior with focused parser, serializer,
  or controller tests when settings mappings change.
