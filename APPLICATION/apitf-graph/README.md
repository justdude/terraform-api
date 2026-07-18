# apitf-graph — «git для Terraform / OpenAPI» (WPF, .NET 8)

Десктоп-приложение на графах: находит методы (операции) в OpenAPI-спеках,
делает **трёхсторонний merge (git-style)** с существующими, либо генерирует спеку
с нуля, и выдаёт Terraform для Azure API Management. Всё детерминировано, **без LLM**.


## Что умеет MVP

- **Граф методов.** Дерево `API → пути → методы`; узлы раскрашены по статусу слияния
  (unchanged / added / modified / removed / conflict).
- **Three-way merge.** Загрузите Base (общий предок), Current (ваш) и Incoming
  (входящий) — движок сам разрешает непротиворечивые изменения и показывает
  только реальные конфликты, как `git merge`.
- **Разрешение конфликтов.** По каждому конфликту выбор: keep ours / take theirs /
  keep base / delete. Кнопка Apply собирает итоговую спеку.
- **Генерация с нуля.** New scratch → Add method (path-параметры из `{...}`
  создаются автоматически) / Remove method.
- **Вывод Terraform.** Generate Terraform → `.tf.json` для `azurerm_api_management_api`
  (bulk import). JSON строится из структуры — без ручного HCL и багов экранирования.
- **JSON и YAML** на вход и выход.

## Архитектура

```
ApitfGraph.sln
├─ src/Apitf.Core        (net8.0)          — вся логика, кроссплатформенная, без UI
│  ├─ SpecModel.cs        OpenAPI-модель (System.Text.Json), JSON/YAML IO, канонизация
│  ├─ Diff.cs             two-way диф операций
│  ├─ Merge.cs            ★ трёхсторонний merge + классификация конфликтов
│  ├─ Editor.cs           init / add / remove (идемпотентно)
│  └─ Terraform.cs        генерация .tf.json (bulk import)
├─ src/Apitf.App         (net8.0-windows)  — WPF: граф на Canvas, панель конфликтов
└─ tests/Apitf.Core.Tests (net8.0, xUnit)  — 21 тест на движок
```

Логика отделена от UI намеренно: ядро `Apitf.Core` тестируется и собирается на любой
ОС; WPF-слой — тонкий. «Сердце» — `Merge.cs` (`ThreeWayMerger`).

## Как работает трёхсторонний merge

Операция идентифицируется ключом `(path, httpMethod)`; содержимое сравнивается по
канонической сериализации (сортировка ключей). Для каждого ключа из объединения
Base/Ours/Theirs:

| Условие | Результат |
|---------|-----------|
| ours == theirs | взять ours (нет конфликта; вкл. «оба удалили») |
| ours == base (не менялось у нас) | взять theirs |
| theirs == base (не менялось у них) | взять ours |
| оба разошлись с base и между собой | **конфликт** |

Виды конфликтов: `ModifyModify`, `AddAdd`, `ModifyDelete`, `DeleteModify`.
Итоговая спека берёт каркас (info/servers/components) из Ours и накладывает
разрешённый набор операций; пустые пути отсекаются; результат канонизируется.

## Сборка и запуск (Windows)

Требуется .NET 8 SDK.

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project src/Apitf.App        # запускает WPF-приложение
```

Запуск приложения возможен только на Windows (WPF). Ядро и тесты — на любой ОС:

```bash
dotnet test                                # 21 тест
```

> На Linux/macOS WPF-проект **собирается** (в csproj включён
> `EnableWindowsTargeting`), что удобно для CI-проверки компиляции, но **не
> запускается** — для запуска нужна Windows.

## Быстрый старт (демо-сценарий)

В папке `examples/` лежит готовый конфликтный кейс:

1. Base… → `examples/base.json`
2. Current (Ours)… → `examples/ours.json`
3. Incoming (Theirs)… → `examples/theirs.json`
4. **Merge ▶** → получите «3 auto-merged, 1 conflict (POST /orders, ModifyModify)».
5. В панели справа выберите для конфликта вариант → **Apply resolutions ✓**.
6. **Generate Terraform…** → `.tf.json`.

## Статус проверки

Собрано и проверено на .NET 8.0.422:

- `dotnet build` решения — **0 warnings, 0 errors** (Core + WPF + Tests).
- WPF-проект компилируется кроссплатформенно (`EnableWindowsTargeting`).
- **26/26** юнит-тестов движка проходят: чистый merge, ours-changed, theirs-changed,
  add/add, modify/modify, modify/delete, delete/modify, both-delete, разрешение
  конфликта, идемпотентность add/remove, детерминизм, генерация Terraform, YAML.
- Сквозной прогон на реальных файлах `examples/`: 3 auto-merged + 1 конфликт →
  после разрешения 4 операции + валидный Terraform.

## Разные environment и id (multi-env генерация)

Слияние методов **не зависит** от среды: операции ключуются по `(path, method)`,
а `apim_name` / `resource_group` / `subscription` / id / revision в спеку не входят —
они живут только в Terraform. Поэтому один общий спек обслуживает все среды.

В тулбаре есть поле **Envs** (например `dev,stage,prod`). При генерации:

- пусто → один ресурс с `${var.apim_name}` / `${var.resource_group_name}`;
- список сред → **env-aware** `.tf.json`: на каждую среду свой aliased-провайдер
  `azurerm` (под разные subscription) и свой ресурс, а значения берутся из типизированной
  карты `var.environments` (`api_name`, `apim_name`, `resource_group`, `subscription_id`,
  `revision`). Все среды импортируют **один и тот же** файл спеки.

Почему отдельный провайдер на среду, а не `for_each`: один `for_each` не может выбирать
провайдера (а значит и subscription) per-instance, поэтому при разных подписках на среду
генерируются per-env блоки с `provider = azurerm.<env>`. Пример вывода —
`examples/orders.multienv.tf.json`.

Заполняете реальные значения в `var.environments` (через `*.tfvars` или дефолт), и один
`terraform apply` раскатывает API во все среды/подписки. Альтернатива — один ресурс +
Terraform workspaces (по `apply` на среду); тогда поле Envs можно оставить пустым.

## Ограничения MVP (осознанные)

- Каркас (components/schemas, servers) берётся из Ours; добавленные только в Theirs
  компоненты не сливаются (операции — сливаются полностью). Расширяемо.
- Конфликты — на уровне операции целиком (не пофайловый строковый three-way внутри
  одной операции). Для API это обычно и нужно.
- Граф — дерево `API→путь→метод` на Canvas (без сторонних граф-движков ради
  нулевых зависимостей); при желании заменяется на MSAGL/GraphX.
- Запуск UI — только Windows (выбранный стек WPF).

## Связь с остальным

Генерируемый `.tf.json` совместим с подходом из `../ADR-001-openapi-to-terraform.md`
(OpenAPI как источник истины + bulk import). Python-утилита `../apitf/` делает то же
из командной строки — это десктоп-версия той же детерминированной логики.
