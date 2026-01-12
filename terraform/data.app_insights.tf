data "azurerm_application_insights" "app_insights" {
  name                = local.app_insights.name
  resource_group_name = local.app_insights.resource_group_name
}
