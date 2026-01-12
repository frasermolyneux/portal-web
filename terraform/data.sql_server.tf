data "azurerm_mssql_server" "sql_server" {
  name                = local.sql_server.name
  resource_group_name = local.sql_server.resource_group_name
}
