locals {
  spec         = jsondecode(file("${path.module}/orders.json"))
  http_methods = ["get", "put", "post", "delete", "patch", "options", "head", "trace"]
  operations = merge([
    for route, methods in local.spec.paths : {
      for verb, op in methods : "${upper(verb)} ${route}" => {
        operation_id = try(op.operationId, "${verb}${replace(route, "/", "_")}")
        method       = upper(verb)
        url_template = route
        display_name = try(op.summary, route)
      } if contains(local.http_methods, verb)
    }
  ]...)
}
output "operation_count" { value = length(local.operations) }
output "operations"      { value = local.operations }
