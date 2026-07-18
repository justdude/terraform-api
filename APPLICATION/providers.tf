terraform {
  required_version = ">= 1.3.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    http = {
      source  = "hashicorp/http"
      version = "~> 3.0"
    }
  }
}

# Azure CLI НЕ требуется. Креды (Service Principal) задаются через
# переменные окружения ARM_SUBSCRIPTION_ID / ARM_TENANT_ID /
# ARM_CLIENT_ID / ARM_CLIENT_SECRET, поэтому блок остаётся пустым.
# Для локальной валидации (terraform validate) креды не нужны вовсе.
provider "azurerm" {
  features {}
}
