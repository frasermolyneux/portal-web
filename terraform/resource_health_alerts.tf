resource "azurerm_monitor_activity_log_alert" "rg_resource_health" {
  name = "${var.workload_name}-${var.environment}-${data.azurerm_resource_group.rg.name}-resource-health"

  resource_group_name = data.azurerm_resource_group.rg.name
  location            = "global"

  scopes      = [data.azurerm_resource_group.rg.id]
  description = "Resource health alert for ${data.azurerm_resource_group.rg.name} resource group"

  criteria {
    category = "ResourceHealth"

    resource_health {
      previous = ["Available"]
    }
  }

  action {
    action_group_id = var.environment == "prd" ? local.action_group_map.critical.id : local.action_group_map.informational.id
  }

  tags = var.tags
}
