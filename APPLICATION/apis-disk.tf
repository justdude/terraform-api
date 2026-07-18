# =====================================================================
# Вариант А: OpenAPI JSON с диска (папка specs/)
# Имя файла кодирует API и environment: <api>.<env>.json
#   например  orders.dev.json  ->  api="orders", env="dev"
# =====================================================================

locals {
  # Все *.json в папке specs/
  spec_files = fileset("${path.module}/specs", "*.json")

  # Собираем карту API из имён файлов
  apis_disk = {
    for f in local.spec_files :
    split(".", f)[0] => {
      path        = split(".", f)[0] # "orders"
      file        = f                # "orders.dev.json"
      environment = split(".", f)[1] # "dev"
    }
  }
}

resource "azurerm_api_management_api" "api" {
  for_each = local.apis_disk

  name                = each.key
  resource_group_name = var.resource_group_name
  api_management_name = var.apim_by_env[each.value.environment]
  revision            = "1"
  display_name        = title(each.key)
  path                = each.value.path
  protocols           = ["https"]

  import {
    content_format = "openapi+json" # "openapi" если YAML
    content_value  = file("${path.module}/specs/${each.value.file}")
  }
}
