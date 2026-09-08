apis = {
  bpc_apis = {
    backend_apis = {
      "orders-api-group" = {
        product = []
        api = [
          {
            apim_resource_group_name         = "rg-apim-qa"
            apim_name                        = "apim-company-qa"
            name                             = "orders-api-qa"
            display_name                     = "Orders API - qa"
            path                             = "orders.qa/v1/api"
            service_url                      = "https://api-qa.company.com/v1/orders/"
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
            operation_id             = "list-orders-qa"
            apim_resource_group_name = "rg-apim-qa"
            apim_name                = "apim-company-qa"
            api_name                 = "orders-api-qa"
            display_name             = "List orders"
            method                   = "GET"
            url_template             = "orders"
            status_code              = "200"
            description              = "Returns all orders"
          },
        ]
      }
    }
  }
}
