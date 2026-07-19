# Azure API Management — platform APIs.
#
# This is a *module input* (tfvars-shaped) file, not resource-style HCL. The
# merge tool parses `key = value` assignments — objects, arrays and heredocs —
# so `resource "azurerm_api_management_api" "x" { … }` blocks are NOT accepted.
# Recognised roots: apis.bpc_apis.backend_apis, apis.backend_apis, or the api
# groups at the document root.
#
# Deliberately harder than samples/orders-dev.tf. It exercises:
#   * two api groups in one file
#   * ${var.…} interpolations in ids, api names and resource groups
#   * an indented heredoc (<<-) policy whose <method> tags must never be
#     mistaken for operations
#   * a non-empty product block and a subscription_key_parameter_names object
#   * comments interleaved between operation blocks
#   * near-miss routes (v1/payments vs v2/payments) and same-route/
#     different-method pairs, which the (method, url_template) identity gates
#     must keep apart

apis = {
  bpc_apis = {
    backend_apis = {
      "payments-api-group" = {
        product = [
          {
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            product_id               = "payments-${var.environment}"
            display_name             = "Payments - ${var.environment}"
            subscription_required    = true
            approval_required        = false
            published                = true
            subscriptions_limit      = null
            description              = "Payment initiation and status APIs"
          },
        ]

        api = [
          {
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            name                     = "payments-api-${var.environment}"
            display_name             = "Payments API - ${var.environment}"
            path                     = "payments.${var.environment}/v1/api"
            service_url              = "https://${var.api_gateway_host}/v1/payments/"
            protocols                = ["https"]
            revision                 = "1"
            soap_pass_through        = false
            subscription_required    = true
            product_id               = "payments-${var.environment}"
            subscription_key_parameter_names = {
              header = "Ocp-Apim-Subscription-Key"
              query  = "subscription-key"
            }
            policy = <<-XML
              <policies>
                <inbound>
                  <base />
                  <rate-limit-by-key calls="120" renewal-period="60"
                                     counter-key="@(context.Request.IpAddress)" />
                  <set-header name="X-Correlation-Id" exists-action="skip">
                    <value>@(context.RequestId.ToString())</value>
                  </set-header>
                  <cors allow-credentials="true">
                    <allowed-origins>
                      <origin>https://portal.company.com</origin>
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
            operation_id             = "${var.operation_prefix}-list-payments-${var.environment}"
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            api_name                 = "payments-api-${var.environment}"
            display_name             = "List payments"
            method                   = "GET"
            url_template             = "v1/payments"
            status_code              = "200"
            description              = "Returns a page of payments"
            request = [
              {
                query_parameter = [
                  {
                    name        = "status"
                    required    = false
                    type        = "string"
                    description = "Filter by settlement status"
                  },
                  {
                    name        = "from"
                    required    = false
                    type        = "string"
                    description = "ISO-8601 lower bound"
                  },
                  {
                    name        = "limit"
                    required    = false
                    type        = "integer"
                    description = "Page size"
                  },
                ]
                header = [
                  {
                    name        = "Accept"
                    required    = false
                    type        = "string"
                    description = "Response media type"
                  },
                ]
              },
            ]
            response = [
              {
                status_code = 200
                description = "OK"
              },
              {
                status_code = 400
                description = "Invalid filter"
              },
            ]
          },

          # v2 keeps the same route shape but returns the enriched projection.
          # Near-miss with v1 above: related enough to align across files, far
          # enough apart that it is not the same operation.
          {
            operation_id             = "${var.operation_prefix}-list-payments-v2-${var.environment}"
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            api_name                 = "payments-api-${var.environment}"
            display_name             = "List payments (v2)"
            method                   = "GET"
            url_template             = "v2/payments"
            status_code              = "200"
            description              = "Returns a page of enriched payments"
            response = [
              {
                status_code = 200
                description = "OK"
              },
            ]
          },
          {
            operation_id             = "${var.operation_prefix}-get-payment-${var.environment}"
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            api_name                 = "payments-api-${var.environment}"
            display_name             = "Get payment by id"
            method                   = "GET"
            url_template             = "v1/payments/{paymentId}"
            status_code              = "200"
            description              = "Returns a single payment"
            response = [
              {
                status_code = 200
                description = "OK"
              },
              {
                status_code = 404
                description = "Not found"
              },
            ]
          },

          # Same route as get-payment, different method — the two must never be
          # collapsed into one operation by the matcher.
          {
            operation_id             = "${var.operation_prefix}-replace-payment-${var.environment}"
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            api_name                 = "payments-api-${var.environment}"
            display_name             = "Replace payment"
            method                   = "PUT"
            url_template             = "v1/payments/{paymentId}"
            status_code              = "200"
            description              = "Replaces a payment in full"
            request = [
              {
                header = [
                  {
                    name        = "If-Match"
                    required    = true
                    type        = "string"
                    description = "Concurrency token"
                  },
                ]
                representation = [
                  {
                    content_type = "application/json"
                    schema_id    = "Payment"
                    type_name    = "Payment"
                  },
                ]
              },
            ]
            response = [
              {
                status_code = 200
                description = "Replaced"
              },
              {
                status_code = 412
                description = "Precondition failed"
              },
            ]
          },
          {
            operation_id             = "${var.operation_prefix}-create-payment-${var.environment}"
            apim_resource_group_name = "${var.stage_group_name}"
            apim_name                = "${var.apim_name}"
            api_name                 = "payments-api-${var.environment}"
            display_name             = "Create payment"
            method                   = "POST"
            url_template             = "v1/payments"
            status_code              = "201"
            description              = "Initiates a payment"
            request = [
              {
                header = [
                  {
                    name        = "Authorization"
                    required    = true
                    type        = "string"
                    description = "Bearer token"
                  },
                  {
                    name        = "Idempotency-Key"
                    required    = true
                    type        = "string"
                    description = "Client-supplied de-duplication key"
                  },
                ]
                representation = [
                  {
                    content_type = "application/json"
                    schema_id    = "PaymentRequest"
                    type_name    = "PaymentRequest"
                  },
                ]
              },
            ]
            response = [
              {
                status_code = 201
                description = "Created"
              },
              {
                status_code = 409
                description = "Duplicate idempotency key"
              },
            ]
          },
        ]
      }

      "inventory-api-group" = {
        product = []

        api = [
          {
            apim_resource_group_name         = "rg-apim-shared"
            apim_name                        = "apim-company-shared"
            name                             = "inventory-api"
            display_name                     = "Inventory API"
            path                             = "inventory/v1/api"
            service_url                      = "https://inventory.internal.company.com/v1/"
            protocols                        = ["https"]
            revision                         = "3"
            soap_pass_through                = false
            subscription_required            = false
            product_id                       = null
            subscription_key_parameter_names = null
          },
        ]

        api_operations = [
          {
            operation_id             = "list-stock-items"
            apim_resource_group_name = "rg-apim-shared"
            apim_name                = "apim-company-shared"
            api_name                 = "inventory-api"
            display_name             = "List stock items"
            method                   = "GET"
            url_template             = "stock"
            status_code              = "200"
            description              = "Returns stock levels per SKU"
          },
          {
            operation_id             = "get-stock-item"
            apim_resource_group_name = "rg-apim-shared"
            apim_name                = "apim-company-shared"
            api_name                 = "inventory-api"
            display_name             = "Get stock item"
            method                   = "GET"
            url_template             = "stock/{sku}"
            status_code              = "200"
            description              = "Returns the stock level for one SKU"
          },
          {
            operation_id             = "adjust-stock-item"
            apim_resource_group_name = "rg-apim-shared"
            apim_name                = "apim-company-shared"
            api_name                 = "inventory-api"
            display_name             = "Adjust stock item"
            method                   = "PATCH"
            url_template             = "stock/{sku}"
            status_code              = "204"
            description              = "Applies a delta to the stock level"
          },
        ]
      }
    }
  }
}
