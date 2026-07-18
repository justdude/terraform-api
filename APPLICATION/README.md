# Импорт OpenAPI в Azure API Management через Terraform

Готовый проект для импорта ваших OpenAPI/Swagger-спек в Azure API Management.
Работает **без Azure CLI**. Без доступа к Azure можно делать всё, кроме `apply`
(сам `apply` создаёт ресурсы в облаке и требует учётных данных Azure).

## Файлы

| Файл | Назначение |
|------|------------|
| `providers.tf` | Провайдеры azurerm + http. Креды через ENV, блок пустой. |
| `variables.tf` | Ресурсная группа, карта `env -> APIM`, список URL-ов. |
| `apis-disk.tf` | **Вариант А** — читает спеки из папки `specs/`. Активен. |
| `apis-url.tf.example` | **Вариант Б** — берёт спеки по URL. Чтобы включить — см. ниже. |
| `specs/orders.dev.json` | Пример спеки. Имя файла = `<api>.<env>.json`. |

Используйте **либо** вариант А, **либо** Б (оба объявляют один ресурс
`azurerm_api_management_api.api` — вместе они конфликтуют).

---

## Что можно сделать без доступа к Azure (локально)

```bash
terraform init        # скачать провайдеры (нужен только интернет)
terraform fmt         # форматирование
terraform validate    # проверка синтаксиса и ссылок — БЕЗ кредов Azure
terraform console     # отладить парсинг: > local.apis_disk
```

`terraform plan` и `terraform apply` потребуют учётных данных Azure
(Service Principal) и существующих APIM-инстансов — это делает тот, у кого
есть доступ, или CI-пайплайн.

---

## Вариант А — спеки с диска (по шагам)

1. Положите ваши JSON-файлы в `specs/`, именуя их `<api>.<env>.json`
   (например `payments.prod.json`). Environment берётся из имени файла.
2. В `variables.tf` поправьте `apim_by_env` под реальные имена ваших APIM
   и `resource_group_name`.
3. Проверьте локально: `terraform init && terraform validate`.
4. Отладьте разбор файлов: `terraform console` → введите `local.apis_disk`.
5. Передайте проект в пайплайн / коллеге для `terraform apply`.
6. **Обновление:** замените JSON в `specs/` и снова `apply` — Terraform увидит
   изменение содержимого (`file()`) и переимпортирует только изменившиеся API.

## Вариант Б — спеки по URL (по шагам)

1. Переименуйте `apis-url.tf.example` → `apis-url.tf`.
2. Удалите или переименуйте `apis-disk.tf` (чтобы не было конфликта ресурса).
3. В `variables.tf` заполните `apis_url` вашими API и ссылками. Environment
   вытаскивается из поддомена URL (`https://<env>.example.com/...`) через regex
   в `apis-url.tf`; если env у вас в пути — поправьте regex там.
4. Проверьте: `terraform init && terraform validate`.
5. `apply` — в пайплайне/у коллеги с доступом к Azure.
6. **Обновление:** просто перезапустите `apply` (можно по расписанию) — спеки
   скачиваются через `data "http"`, изменившиеся API переимпортируются.

---

## Аутентификация (когда дойдёт до apply, без Azure CLI)

Задайте переменные окружения Service Principal — `az login` не нужен:

```bash
export ARM_SUBSCRIPTION_ID="..."
export ARM_TENANT_ID="..."
export ARM_CLIENT_ID="..."
export ARM_CLIENT_SECRET="..."
```

## Форматы content_format

| Спека | Значение |
|-------|----------|
| OpenAPI 3.0 JSON | `openapi+json` |
| OpenAPI 3.0 YAML | `openapi` |
| OpenAPI 3.0 по ссылке | `openapi-link` |
| Swagger 2.0 JSON | `swagger-json` |
| Swagger 2.0 по ссылке | `swagger-link-json` |
