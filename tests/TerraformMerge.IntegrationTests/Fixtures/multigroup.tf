apis = {
  bpc_apis = {
    backend_apis = {
      "orders-api-group" = {
        product = []
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
        ]
      }

      "inventory-api-group" = {
        product = []
        api_operations = [
          {
            operation_id             = "list-stock-dev"
            apim_resource_group_name = "rg-apim-dev"
            apim_name                = "apim-company-dev"
            api_name                 = "inventory-api-dev"
            display_name             = "List stock"
            method                   = "GET"
            url_template             = "stock"
            status_code              = "200"
            description              = "Returns stock levels"
          },
        ]
      }
    }
  }
}
