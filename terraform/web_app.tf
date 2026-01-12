resource "azurerm_linux_web_app" "app" {
  name = local.web_app_name

  tags = var.tags

  resource_group_name = data.azurerm_resource_group.rg.name
  location            = data.azurerm_resource_group.rg.location

  service_plan_id = data.azurerm_service_plan.sp.id

  https_only = true

  identity {
    type         = "UserAssigned"
    identity_ids = [local.web_identity.id]
  }

  key_vault_reference_identity_id = local.web_identity.id

  site_config {
    application_stack {
      dotnet_version = "9.0"
    }

    ftps_state = "Disabled"
    always_on  = true

    minimum_tls_version = "1.2"

    health_check_path                 = "/api/health"
    health_check_eviction_time_in_min = 5
  }

  app_settings = {
    "AzureAppConfiguration__Endpoint"                = local.app_configuration_endpoint
    "AzureAppConfiguration__ManagedIdentityClientId" = local.web_identity.client_id
    "AzureAppConfiguration__Environment"             = var.environment

    "AZURE_CLIENT_ID" = local.web_identity.client_id

    "minTlsVersion"                              = "1.2"
    "APPLICATIONINSIGHTS_CONNECTION_STRING"      = data.azurerm_application_insights.app_insights.connection_string
    "ApplicationInsightsAgent_EXTENSION_VERSION" = "~3"
    "ASPNETCORE_ENVIRONMENT"                     = var.environment == "prd" ? "Production" : "Development"
    "WEBSITE_RUN_FROM_PACKAGE"                   = "1"

    "RepositoryApi__BaseUrl"             = local.repository_api.api_management.endpoint
    "RepositoryApi__ApplicationAudience" = local.repository_api.application.primary_identifier_uri

    "ServersIntegrationApi__BaseUrl"             = local.servers_integration_api.api_management.endpoint
    "ServersIntegrationApi__ApplicationAudience" = local.servers_integration_api.application.primary_identifier_uri

    "GeoLocationApi__BaseUrl"             = var.geo_location_api.base_url
    "GeoLocationApi__ApiKey"              = format("@Microsoft.KeyVault(SecretUri=%s)", var.geo_location_api.keyvault_primary_ref)
    "GeoLocationApi__ApplicationAudience" = var.geo_location_api.application_audience

    "sql_connection_string" = format("Server=tcp:%s;Authentication=Active Directory Default; Database=%s;User ID=%s;", data.azurerm_mssql_server.sql_server.fully_qualified_domain_name, local.sql_database_name, local.web_identity.client_id)

    // https://learn.microsoft.com/en-us/azure/azure-monitor/profiler/profiler-azure-functions#app-settings-for-enabling-profiler
    "APPINSIGHTS_PROFILERFEATURE_VERSION"  = "1.0.0"
    "DiagnosticServices_EXTENSION_VERSION" = "~3"
  }
}
