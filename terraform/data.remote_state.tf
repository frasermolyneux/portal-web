data "terraform_remote_state" "platform_workloads" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.platform_workloads_state.resource_group_name
    storage_account_name = var.platform_workloads_state.storage_account_name
    container_name       = var.platform_workloads_state.container_name
    key                  = var.platform_workloads_state.key
    use_oidc             = true
    subscription_id      = var.platform_workloads_state.subscription_id
    tenant_id            = var.platform_workloads_state.tenant_id
  }
}

data "terraform_remote_state" "platform_monitoring" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.platform_monitoring_state.resource_group_name
    storage_account_name = var.platform_monitoring_state.storage_account_name
    container_name       = var.platform_monitoring_state.container_name
    key                  = var.platform_monitoring_state.key
    use_oidc             = true
    subscription_id      = var.platform_monitoring_state.subscription_id
    tenant_id            = var.platform_monitoring_state.tenant_id
  }
}

data "terraform_remote_state" "portal_environments" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.portal_environments_state.resource_group_name
    storage_account_name = var.portal_environments_state.storage_account_name
    container_name       = var.portal_environments_state.container_name
    key                  = var.portal_environments_state.key
    use_oidc             = true
    subscription_id      = var.portal_environments_state.subscription_id
    tenant_id            = var.portal_environments_state.tenant_id
  }
}

data "terraform_remote_state" "portal_core" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.portal_core_state.resource_group_name
    storage_account_name = var.portal_core_state.storage_account_name
    container_name       = var.portal_core_state.container_name
    key                  = var.portal_core_state.key
    use_oidc             = true
    subscription_id      = var.portal_core_state.subscription_id
    tenant_id            = var.portal_core_state.tenant_id
  }
}
