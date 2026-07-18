apis = {
  bpc_apis = {
    backend_apis = {
      "orders-api-group" = {
        product = []
        api = [
          {
            apim_resource_group_name         = "rg-apim-staging"
            apim_name                        = "apim-company-staging"
            name                             = "orders-api-staging"
            display_name                     = "Orders API - staging"
            path                             = "orders.staging/v1/api"
            service_url                      = "https://api-staging.company.com/v1/orders/"
            protocols                        = ["https"]
            revision                         = "1"
            soap_pass_through                = false
            subscription_required            = false
            product_id                       = null
            subscription_key_parameter_names = null
          },
        ]

        api_operations = [
          {
            operation_id             = "list-orders-staging"
            apim_resource_group_name = "rg-apim-staging"
            apim_name                = "apim-company-staging"
            api_name                 = "orders-api-staging"
            display_name             = "List orders"
            method                   = "GET"
            url_template             = "orders"
            status_code              = "200"
            description              = "Returns all orders"
          },
          {
            operation_id             = "get-order-staging"
            apim_resource_group_name = "rg-apim-staging"
            apim_name                = "apim-company-staging"
            api_name                 = "orders-api-staging"
            display_name             = "Get order by id"
            method                   = "GET"
            url_template             = "orders/{orderId}"
            status_code              = "200"
            description              = "Returns a single order"
          },
          {
            operation_id             = "cancel-order-staging"
            apim_resource_group_name = "rg-apim-staging"
            apim_name                = "apim-company-staging"
            api_name                 = "orders-api-staging"
            display_name             = "Cancel order"
            method                   = "POST"
            url_template             = "orders/{orderId}/cancel"
            status_code              = "202"
            description              = "Cancels an order"
          },
        ]
      }
    }
  }
}
