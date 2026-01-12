data "azurerm_resource_group" "rg" {
  name = local.workload_resource_group.name
}
