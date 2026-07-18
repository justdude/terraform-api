variable "resource_group_name" {
  type        = string
  description = "Имя ресурсной группы, где живут APIM-инстансы"
  default     = "rg-api"
}

# Сопоставление environment -> имя существующего APIM-инстанса.
variable "apim_by_env" {
  type = map(string)
  default = {
    dev   = "apim-dev"
    stage = "apim-stage"
    prod  = "apim-prod"
  }
}

# ---- Только для варианта по URL (apis-url.tf) ----
# Список API: ключ = имя API, path = префикс роута, url = ссылка на OpenAPI JSON.
# Environment вытаскивается из поддомена URL через regex в apis-url.tf.
variable "apis_url" {
  type = map(object({
    path = string
    url  = string
  }))
  default = {
    orders = {
      path = "orders"
      url  = "https://dev.example.com/orders/openapi.json"
    }
    payments = {
      path = "payments"
      url  = "https://prod.example.com/payments/openapi.json"
    }
  }
}
