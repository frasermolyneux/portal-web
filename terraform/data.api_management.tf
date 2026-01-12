data "azurerm_api_management" "api_management" {
  name                = local.api_management.name
  resource_group_name = local.api_management.resource_group_name
}
