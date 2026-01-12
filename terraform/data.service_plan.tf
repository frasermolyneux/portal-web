data "azurerm_service_plan" "sp" {
  name                = local.app_service_plan.name
  resource_group_name = local.app_service_plan.resource_group_name
}
