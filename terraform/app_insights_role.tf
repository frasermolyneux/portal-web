resource "azurerm_role_assignment" "web_app_insights_reader" {
  scope                = data.azurerm_application_insights.app_insights.id
  role_definition_name = "Monitoring Reader"
  principal_id         = local.web_identity.principal_id
}
