# apitf — детерминированный OpenAPI → Terraform helper (без LLM)

Тонкая CLI для управления методами OpenAPI и генерации Terraform для Azure API
Management. Всё детерминировано: парсеры + валидатор + сериализация, никаких
языковых моделей.

## Установка

```bash
pip install -r requirements.txt   # ruamel.yaml, openapi-spec-validator
python apitf.py --help
```

Работает с Python 3.8+. Файлы `.json` и `.yaml`/`.yml` определяются по расширению.

## Команды

| Команда | Что делает |
|---------|------------|
| `init <file> --title T` | Создать пустую валидную OpenAPI 3.0 спеку с нуля |
| `add <file> <METHOD> <path>` | Добавить операцию (идемпотентно); path-параметры из `{...}` создаются автоматически |
| `remove <file> <METHOD> <path>` | Удалить операцию (идемпотентно) |
| `list <file>` | Показать все операции |
| `validate <file>` | Проверить спеку через openapi-spec-validator |
| `gen <file> --name N --out f.tf.json` | Сгенерировать Terraform JSON для APIM bulk import |

Флаги `add`: `--summary`, `--operation-id`, `--force` (перезаписать существующую).

## Пример: сгенерировать API с нуля

```bash
python apitf.py init specs/orders.json --title "Orders API"
python apitf.py add specs/orders.json GET  /orders          --summary "List orders"
python apitf.py add specs/orders.json POST /orders          --summary "Create order"
python apitf.py add specs/orders.json GET  "/orders/{id}"   --summary "Get order"
python apitf.py validate specs/orders.json          # -> VALID
python apitf.py gen specs/orders.json --name orders --out orders.tf.json
```

## Пример: обновить существующий файл

```bash
python apitf.py add    specs/orders.json DELETE "/orders/{id}" --summary "Delete order"
python apitf.py remove specs/orders.json POST   /orders
python apitf.py validate specs/orders.json
```

## Гарантии (проверены прогоном)

- **Детерминизм:** одинаковый ввод → байт-в-байт одинаковый файл.
- **Обратимость:** `add X` затем `remove X` возвращает исходный файл байт-в-байт.
- **Идемпотентность:** повторный `add`/`remove` ничего не ломает.
- **Валидация:** ловит битый JSON/YAML, отсутствие `responses`, необъявленные
  path-параметры и пр.
- **Без HCL-экранирования:** `.tf.json` строится из структуры (`json.dumps`),
  а не из строк — Terraform читает его нативно.

## Как это связано с Terraform

OpenAPI-файл — единственный источник истины. Terraform потребляет его одним из
способов (см. `examples/`):

1. **`file()` bulk import** — `azurerm_api_management_api` со ссылкой на файл;
   APIM сам разворачивает все методы. Меньше всего кода/состояния.
2. **`.tf.json`** (`apitf gen`) — коммитимый per-API Terraform без ручного HCL.
3. **`jsondecode(file)`** (`examples/pure-terraform-jsondecode.tf`) — Terraform сам
   перечисляет методы спеки, вообще без кодогена.

## Рекомендуемый workflow (CI/pre-commit)

```bash
python apitf.py validate specs/<api>.json && terraform validate
```

Подробное обоснование выбора подхода и сравнение 10 вариантов — в
`../ADR-001-openapi-to-terraform.md`.
