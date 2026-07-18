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
            policy = <<XML
<policies>
  <inbound>
    <base />
    <cors allow-credentials="true">
      <allowed-origins>
        <origin>https://portal.dev.company.com</origin>
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
        <method>POST</method>
        <method>PUT</method>
        <method>DELETE</method>
      </allowed-methods>
      <allowed-headers>
        <header>*</header>
      </allowed-headers>
    </cors>
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
XML
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
            request = [
              {
                query_parameter = [
                  {
                    name        = "status"
                    required    = false
                    type        = "string"
                    description = "Filter by status"
                  },
                  {
                    name        = "limit"
                    required    = false
                    type        = "integer"
                    description = "Page size"
                  },
                ]
              },
            ]
            response = [
              {
                status_code = 200
                description  = "OK"
              },
            ]
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
            response = [
              {
                status_code = 200
                description  = "OK"
              },
              {
                status_code = 404
                description  = "Not found"
              },
            ]
          },
          {
            operation_id             = "create-order-dev"
            apim_resource_group_name = "rg-apim-dev"
            apim_name                = "apim-company-dev"
            api_name                 = "orders-api-dev"
            display_name             = "Create order"
            method                   = "POST"
            url_template             = "orders"
            status_code              = "201"
            description              = "Creates an order"
            request = [
              {
                header = [
                  {
                    name        = "Authorization"
                    required    = true
                    type        = "string"
                    description = "Bearer token"
                  },
                ]
                representation = [
                  {
                    content_type = "application/json"
                  },
                ]
              },
            ]
            response = [
              {
                status_code = 201
                description  = "Created"
              },
            ]
          },
        ]
      }
    }
  }
}
