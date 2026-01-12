resource "azurerm_mssql_database" "database" {
  name = local.sql_database_name

  server_id = data.azurerm_mssql_server.sql_server.id
  tags      = var.tags

  sku_name = "Basic"

  max_size_gb = 2
}
