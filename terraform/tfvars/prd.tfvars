workload_name = "portal-web"
environment   = "prd"
location      = "uksouth"

subscription_id = "32444f38-32f4-409f-889c-8e8aa2b5b4d1"

platform_workloads_state = {
  resource_group_name  = "rg-tf-platform-workloads-prd-uksouth-01"
  storage_account_name = "sadz9ita659lj9xb3"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
  subscription_id      = "7760848c-794d-4a19-8cb2-52f71a21ac2b"
  tenant_id            = "e56a6947-bb9a-4a6e-846a-1f118d1c3a14"
}

platform_monitoring_state = {
  resource_group_name  = "rg-tf-platform-monitoring-prd-uksouth-01"
  storage_account_name = "sa74f04c5f984e"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
  subscription_id      = "7760848c-794d-4a19-8cb2-52f71a21ac2b"
  tenant_id            = "e56a6947-bb9a-4a6e-846a-1f118d1c3a14"
}

portal_environments_state = {
  resource_group_name  = "rg-tf-portal-environments-prd-uksouth-01"
  storage_account_name = "sad74a6da165e7"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
  subscription_id      = "7760848c-794d-4a19-8cb2-52f71a21ac2b"
  tenant_id            = "e56a6947-bb9a-4a6e-846a-1f118d1c3a14"
}

portal_core_state = {
  resource_group_name  = "rg-tf-portal-core-prd-uksouth-01"
  storage_account_name = "sa2e3e95eb7965"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
  subscription_id      = "7760848c-794d-4a19-8cb2-52f71a21ac2b"
  tenant_id            = "e56a6947-bb9a-4a6e-846a-1f118d1c3a14"
}

geo_location_api = {
  base_url               = "https://apim-geo-location-prd-swedencentral-6f10eaac01a0.azure-api.net/geolocation"
  application_audience   = "api://e56a6947-bb9a-4a6e-846a-1f118d1c3a14/geolocation-api-prd"
  keyvault_primary_ref   = "https://kv-18ac60675297-swe.vault.azure.net/secrets/portal-web-prd-apim-subscription-key/"
  keyvault_secondary_ref = "https://kv-18ac60675297-swe.vault.azure.net/secrets/portal-web-prd-apim-subscription-key-secondary/"
}

dns_subscription_id     = "db34f572-8b71-40d6-8f99-f29a27612144"
dns_resource_group_name = "rg-platform-dns-prd-uksouth-01"
dns_subdomain           = "portal"
dns_zone_name           = "xtremeidiots.com"

tags = {
  Environment = "prd",
  Workload    = "portal-web",
  DeployedBy  = "GitHub-Terraform",
  Git         = "https://github.com/frasermolyneux/portal-web"
}
