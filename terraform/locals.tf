locals {
  # Remote State References
  workload_resource_groups = {
    for location in [var.location] :
    location => data.terraform_remote_state.platform_workloads.outputs.workload_resource_groups[var.workload_name][var.environment].resource_groups[lower(location)]
  }

  workload_backend = try(
    data.terraform_remote_state.platform_workloads.outputs.workload_terraform_backends[var.workload_name][var.environment],
    null
  )

  workload_administrative_unit = try(
    data.terraform_remote_state.platform_workloads.outputs.workload_administrative_units[var.workload_name][var.environment],
    null
  )

  workload_resource_group = local.workload_resource_groups[var.location]

  action_group_map = {
    critical      = data.terraform_remote_state.platform_monitoring.outputs.monitor_action_groups.critical
    high          = data.terraform_remote_state.platform_monitoring.outputs.monitor_action_groups.high
    moderate      = data.terraform_remote_state.platform_monitoring.outputs.monitor_action_groups.moderate
    low           = data.terraform_remote_state.platform_monitoring.outputs.monitor_action_groups.low
    informational = data.terraform_remote_state.platform_monitoring.outputs.monitor_action_groups.informational
  }

  app_configuration_endpoint = data.terraform_remote_state.portal_environments.outputs.app_configuration.endpoint

  managed_identities      = data.terraform_remote_state.portal_environments.outputs.managed_identities
  web_identity            = local.managed_identities["web"]
  api_management_identity = local.managed_identities["api_management"]

  api_management          = data.terraform_remote_state.portal_environments.outputs.api_management
  repository_api          = data.terraform_remote_state.portal_environments.outputs.repository_api
  servers_integration_api = data.terraform_remote_state.portal_environments.outputs.servers_integration_api
  app_insights            = data.terraform_remote_state.portal_core.outputs.app_insights
  app_service_plan        = data.terraform_remote_state.portal_core.outputs.app_service_plans["apps"]
  sql_server              = data.terraform_remote_state.portal_core.outputs.sql_server

  # Local Resource Naming
  web_app_name      = "app-portal-web-${var.environment}-${var.location}-${random_id.environment_id.hex}"
  sql_database_name = "portal-web-${random_id.environment_id.hex}"
}
