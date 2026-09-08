apis = {
  bpc_apis = {
    backend_apis = {
      "orders-api-group" = {
        product = []
        api = [
          {
            apim_resource_group_name         = "rg-apim-dev"
            apim_name                        = "apim-company-dev"
            name                             = "orders-api-dev"
            display_name                     = "Orders API - dev"
            path                             = "orders.dev/v1/api"
            service_url                      = "https://api-dev.company.com/v1/orders/"
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
            operation_id             = "list-orders-dev"
            apim_resource_group_name = "rg-apim-dev"
            apim_name                = "apim-company-dev"
            api_name                 = "orders-api-dev"
            display_name             = "List orders"
            method                   = "GET"
            url_template             = "orders"
            status_code              = "200"
            description              = "Returns all orders"
          },
          {
            operation_id             = "get-order-dev"
            apim_resource_group_name = "rg-apim-dev"
            apim_name                = "apim-company-dev"
            api_name                 = "orders-api-dev"
            display_name             = "Get order by id"
            method                   = "GET"
            url_template             = "orders/{orderId}"
            status_code              = "200"
            description              = "Returns a single order"
          },
        ]
      }
    }
  }
}
