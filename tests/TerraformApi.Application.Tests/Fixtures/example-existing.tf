apis = {
  bpc_apis = {
    backend_apis = {
      "${api_group_name}" = {
        product = []
        api = [
          {
            apim_resource_group_name         = "${stage_group_name}"
            apim_name                        = "${apim_name}"
            name                             = "${api_name}-${env}"
            display_name                     = "${api_display_name} - ${env}"
            path                             = "${api_path_prefix}.${env}/v1/${api_path_suffix}"
            service_url                      = "https://${api_gateway_host}/${api_version}/${backend_service_path}/"
            protocols                        = ["https"]
            revision                         = "${api_revision}"
            soap_pass_through                = false
            subscription_required            = false
            product_id                       = "${product_id}"
            subscription_key_parameter_names = null
            policy = <<XML
<policies>
  <inbound>
    <base />
    <cors allow-credentials="true">
      <allowed-origins>
        <origin>https://${frontend_host}.${env}.${company_domain}</origin>
        <origin>https://${local_dev_host}:${local_dev_port}</origin>
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
        <method>POST</method>
        <method>PUT</method>
        <method>DELETE</method>
        <method>OPTIONS</method>
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
            operation_id             = "${operation_prefix}-${env}"
            apim_resource_group_name = "${stage_group_name}"
            apim_name                = "${apim_name}"
            api_name                 = "${api_name}-${env}"
            display_name             = "${operation_display_name}"
            method                   = "GET"
            url_template             = "${operation_path}"
            status_code              = "200"
            description              = ""
          },
        ]
      }
    }
  }
}
