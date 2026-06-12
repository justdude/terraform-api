# План реализации: APIM Terraform Sync Engine (append-only)

> **Аудитория**: LLM (Claude Opus) и senior .NET разработчик, реализующие фичу.  
> **Контекст**: расширение существующего проекта `terraform-api` (.NET 10, Clean Architecture: Domain / Application / Api / Mcp).  
> **Цель**: добавить движок, который умеет (а) генерировать APIM Terraform с нуля из OpenAPI и (б) **append-only** синхронизировать существующий Terraform-конфиг с актуальным OpenAPI, **никогда ничего не удаляя**, плюс детектить дубликаты по нескольким ключам и давать полноценный отчёт.

> ⚠️ **REVISION 1 (2026-06-12)** — добавлено:
>
> - **§REV-1.2** — Полный реестр плейсхолдеров и `ApimTemplateProfile` (что шаблонизируем при генерации из OpenAPI).
> - **§REV-1.3** — Детекция стиля существующего файла + auto-grouping по `(apim_resource_group_name, api_name)`.
> - **§REV-1.4** — Группировка распарсенных API.
> - **§REV-1.5** — Поддержка комментариев в AST + формат комментариев перед каждой операцией.
> - **§REV-2** — Подробный список изменений MCP-сервера: 3 новых тула + обновления существующих.
> - Все базовые разделы плана (§1–§9) остаются в силе, REVISION 1 их **дополняет** и в нескольких местах **уточняет**.

---

## 0. Контекст и ограничения, которые меняют дизайн

### 0.1. Структура целевого HCL не плоская

В рабочем примере, который дал пользователь, конфигурация выглядит так:

```hcl
apis = {
  bpc_apis = {
    backend_apis = {
      "${api_group_name}" = {
        product        = []
        api            = [ { ... } ]
        api_operations = [ { ... }, { ... } ]
      }
    }
  }
}
```

Это **трёхуровневая вложенность под именованным ключом** (`apis.bpc_apis.backend_apis.<api_group>`), и внутри значения — массивы объектов. Текущий генератор (`TerraformGeneratorService`) и мерджер (`TerraformMergerService`) предполагают плоский `<api_group_name> = { ... }` верхнего уровня — это работает для «с нуля», но **не масштабируется** на реальный файл проекта.

**Следствие**: парсер обязан понимать произвольный путь к узлу с `api_operations` и `api`, а не искать их регулярками от начала файла.

### 0.2. Везде `${...}` интерполяции Terraform

Все значения в примере — это интерполяции: `name = "${api_name}-${env}"`, `operation_id = "${operation_prefix}-${env}"`, `url_template = "${operation_path}"`. Это означает:

- Текущий regex `operation_id\s*=\s*"([^"]+)"` вернёт строку `${operation_prefix}-${env}` целиком. Сама по себе она валидный «токен», но **сравнить операции между средами по такому токену нельзя**, потому что `${env}` в dev и prod разный.
- Для матчинга и detection нужны **два режима**:
  - **Structural mode** — сравниваем токены как есть (две операции «одинаковы», если их HCL-выражения текстуально совпадают). Подходит для round-trip в пределах одного файла.
  - **Resolved mode** — пользователь даёт `Dictionary<string,string>` со значениями переменных (например, `env=dev`, `api_name=bpc`), мы подставляем и сравниваем уже разрешённые строки. Подходит для синхронизации с конкретной средой.

### 0.3. Append-only — не merge, а enrichment

Ограничение «нельзя удалять, можно только добавлять `url`-параметры, тип метода, параметры метода» — это не классический merge. Это:

- Операция, которой нет в OpenAPI, но есть в Terraform → **остаётся как есть, всегда**.
- Операция, которая есть в обоих → **дефолт: ничего не меняем**; конкретные поля можно дополнить, **только если в существующем Terraform это поле отсутствует или пустое** (политика `EnrichOnly`). Поля, которые могут быть «дополнены» при наличии сигнала из OpenAPI (`request.header[]`, `request.query[]`, `responses[]`) — это **append к коллекциям**, но не replace.
- Операция, которая есть в OpenAPI, но не в Terraform → **добавляем целиком** (append на верхнем уровне).

Это надо выразить **per-field политикой** + **per-collection-element политикой**, и сделать конфигурируемой.

### 0.4. Что уже есть в загруженной документации

Существующие файлы (`OPERATION_TRACKING_*`, `EXECUTIVE_SUMMARY`, `IMPLEMENTATION_ROADMAP` и т. д.) дают:

- Граф операций (Node / Graph / Statistics)
- Статусы (`Included | Modified | Excluded | Blocked | Skipped | Deprecated`)
- Трекинг-репорт с дельтами
- Экспорт в Mermaid / CSV / JSON

Этот скелет **используем как есть** для отчётности. Но всё, что касается извлечения операций из HCL и их сопоставления, **полностью переписываем**: regex-подход из текущего `TerraformMergerService` неприменим.

---

## 1. Архитектура решения

### 1.1. Слои и новые модули

```
TerraformApi.Domain
└── Models
    ├── Hcl/                              ← НОВОЕ: AST HCL
    │   ├── HclDocument.cs
    │   ├── HclNode.cs (abstract)
    │   ├── HclObject.cs
    │   ├── HclArray.cs
    │   ├── HclLiteral.cs
    │   ├── HclInterpolation.cs
    │   ├── HclHeredoc.cs
    │   ├── HclAssignment.cs
    │   ├── HclComment.cs                 ← НОВОЕ (см. §REV-1.5)
    │   └── HclObjectItem.cs (abstract)   ← НОВОЕ (assignment | comment)
    ├── Sync/                             ← НОВОЕ: модели синхронизации
    │   ├── OperationFingerprint.cs
    │   ├── OperationMatchKey.cs (enum)
    │   ├── OperationMatchStrategy.cs
    │   ├── FieldMergePolicy.cs (enum)
    │   ├── MergePolicy.cs
    │   ├── OperationDiff.cs
    │   ├── FieldDiff.cs
    │   ├── DuplicateGroup.cs
    │   ├── SyncReport.cs
    │   ├── SyncResult.cs
    │   ├── ApimTemplateProfile.cs        ← НОВОЕ (см. §REV-1.2)
    │   ├── CorsTemplateVariables.cs      ← НОВОЕ
    │   ├── DetectedProfile.cs            ← НОВОЕ (см. §REV-1.3)
    │   ├── StylingConfidence.cs (enum)   ← НОВОЕ
    │   ├── ApimApiGroupKey.cs            ← НОВОЕ (см. §REV-1.4)
    │   └── OperationCommentSpec.cs       ← НОВОЕ (см. §REV-1.5)
    ├── Apim/
    │   ├── ParsedApimDocument.cs
    │   ├── ParsedApiGroup.cs
    │   ├── ParsedApi.cs
    │   ├── ParsedApiOperation.cs
    │   └── HclValueRef.cs
    └── Tracking/                          ← из существующего плана
        ├── OperationExecutionGraph.cs
        └── OperationTrackingReport.cs

TerraformApi.Domain
└── Interfaces
    ├── IHclParser.cs                     ← НОВОЕ
    ├── IHclWriter.cs                     ← НОВОЕ
    ├── IApimTerraformReader.cs           ← НОВОЕ
    ├── IApimTerraformWriter.cs           ← НОВОЕ
    ├── IOperationMatcher.cs              ← НОВОЕ
    ├── IDuplicateDetector.cs             ← НОВОЕ
    ├── IAppendOnlySynchronizer.cs        ← НОВОЕ
    ├── IApimTemplateProfileDetector.cs   ← НОВОЕ (см. §REV-1.3)
    ├── IApimTemplateProfileApplier.cs    ← НОВОЕ (см. §REV-1.2.5)
    ├── IOperationCommentBuilder.cs       ← НОВОЕ (см. §REV-1.5)
    └── IOperationExecutionGraphBuilder.cs ← из существующего плана

TerraformApi.Application
└── Services
    ├── Hcl/
    │   ├── HclLexer.cs                   ← НОВОЕ
    │   ├── HclParserService.cs           ← НОВОЕ
    │   └── HclWriterService.cs           ← НОВОЕ
    ├── Apim/
    │   ├── ApimTerraformReaderService.cs ← НОВОЕ
    │   └── ApimTerraformWriterService.cs ← НОВОЕ
    ├── Sync/
    │   ├── OperationMatcherService.cs    ← НОВОЕ
    │   ├── DuplicateDetectorService.cs   ← НОВОЕ
    │   ├── AppendOnlySynchronizerService.cs ← НОВОЕ
    │   ├── TerraformInterpolationResolver.cs ← НОВОЕ
    │   ├── ApimTemplateProfileDetectorService.cs ← НОВОЕ (см. §REV-1.3)
    │   ├── ApimTemplateProfileApplierService.cs  ← НОВОЕ
    │   └── OperationCommentBuilderService.cs     ← НОВОЕ (см. §REV-1.5)
    ├── OperationExecutionGraphBuilderService.cs (из существующего плана)
    └── ConversionOrchestratorService.cs (модифицируется: добавляются Sync(), Analyze(), ApplyProfile())

src/TerraformApi.Mcp/Tools/                ← обновления (см. §REV-2)
    ├── SyncTool.cs                       ← НОВОЕ
    ├── AnalyzeTool.cs                    ← НОВОЕ
    ├── ApplyTemplateProfileTool.cs       ← НОВОЕ
    ├── ConvertTool.cs                    ← ОБНОВЛЕНИЕ (templateProfile param)
    └── UpdateTool.cs                     ← ОБНОВЛЕНИЕ (делегирует в Sync)
```

### 1.2. Поток данных (Сценарий 2: Sync)

```
┌──────────────────┐    ┌──────────────────────┐
│  OpenAPI JSON    │    │  Существующий HCL    │
└────────┬─────────┘    └──────────┬───────────┘
         │                         │
         ▼                         ▼
   OpenApiParser            HclParserService
         │                         │
         ▼                         ▼
   ApimConfiguration       HclDocument (AST)
   (operations[])                  │
         │                         ▼
         │              ApimTerraformReaderService
         │                  (вытаскивает api/api_operations
         │                   с сохранением путей в AST)
         │                         │
         │                         ▼
         │                ParsedApimDocument
         │                  ├── ApiGroups[]
         │                  │   └── ParsedApiOperation[]
         │                  └── HclDocument (исходный AST для round-trip)
         │                         │
         └──────┬──────────────────┘
                ▼
       OperationMatcherService
       (по выбранной MatchStrategy)
                │
                ▼
       MatchResult{ Added, Existing, Removed-in-Tf-only }
                │
                ▼
       DuplicateDetectorService
       (отдельно для всего, что в HCL)
                │
                ▼
       AppendOnlySynchronizerService
       (применяет MergePolicy к AST)
                │
                ▼
       Модифицированный HclDocument
                │
                ▼
       HclWriterService → string (валидный HCL)
                │
                ▼
       SyncResult { TerraformConfig, SyncReport, ExecutionGraph }
```

### 1.3. Ключевые архитектурные решения

| Решение                                                              | Обоснование                                                                                                                                                                                                                       |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Свой минимальный HCL-парсер, не сторонняя либа                       | Нужно поддержать узкое подмножество (`apis.bpc_apis.backend_apis...`, heredocs, интерполяции). Существующие .NET-библиотеки HCL заброшены/неполны. Минимальный лексер + рекурсивный парсер — ~600 строк, полностью контролируемо. |
| AST сохраняется в `HclDocument` целиком и переиспользуется на запись | Round-trip без потери комментариев, форматирования heredoc'ов и интерполяций. Writer работает поверх AST, а не reflection'ом по моделям.                                                                                          |
| Интерполяции — отдельный тип узла `HclInterpolation`                 | Не теряются при сравнении, можно опционально резолвить, можно сравнивать «текстуально».                                                                                                                                           |
| `OperationFingerprint` — мульти-ключевой record                      | Не одна стратегия матчинга, а композиция ключей. Пользователь сам выбирает приоритет.                                                                                                                                             |
| `MergePolicy` per-field + per-collection                             | Append-only нельзя выразить одним флагом — нужны раздельные политики для скалярных полей, для коллекций (headers, query params, responses) и для блоков (`request`, `policy`).                                                    |
| Парсер APIM-структуры **отделён** от HCL-парсера                     | HCL-парсер ничего не знает про APIM. `ApimTerraformReaderService` навигирует по AST по конкретным путям. Это позволяет тестировать слои независимо.                                                                               |
| `OperationExecutionGraphBuilder` — потребитель, а не источник        | Граф строится **после** Sync на основе `SyncReport`. Никакого regex'а внутри builder'а быть не должно.                                                                                                                            |
| Старый `TerraformMergerService` остаётся как тонкая обёртка          | Не ломаем существующие endpoint'ы `/api/convert/update` и MCP-тулы. Они начинают делегировать в `AppendOnlySynchronizerService` через адаптер.                                                                                    |

---

## 2. Доменные модели — детальная спецификация

### 2.1. HCL AST

Файл: `src/TerraformApi.Domain/Models/Hcl/HclDocument.cs` и соседние.

```csharp
namespace TerraformApi.Domain.Models.Hcl;

/// Корневой узел: либо последовательность присваиваний на верхнем уровне,
/// либо обёртка вокруг одного объекта.
public sealed record HclDocument
{
    public List<HclAssignment> RootAssignments { get; init; } = [];
    /// Опциональный raw-source для диагностики/восстановления комментариев.
    public string? OriginalSource { get; init; }
}

public abstract record HclNode
{
    /// 1-based позиция в исходнике (для ошибок).
    public int Line { get; init; }
    public int Column { get; init; }
}

public sealed record HclAssignment : HclNode
{
    public required string Key { get; init; }    // например, "operation_id"
    public required HclValue Value { get; init; }
    /// True если ключ был в кавычках: `"my-api-group" = { ... }`
    public bool KeyIsQuoted { get; init; }
}

public abstract record HclValue : HclNode;

public sealed record HclObject : HclValue
{
    public List<HclAssignment> Assignments { get; init; } = [];

    public HclValue? Get(string key) =>
        Assignments.FirstOrDefault(a => a.Key == key)?.Value;
}

public sealed record HclArray : HclValue
{
    public List<HclValue> Items { get; init; } = [];
}

/// Строка/число/bool/null.
public sealed record HclLiteral : HclValue
{
    public required string RawValue { get; init; }   // как было в исходнике
    public required HclLiteralKind Kind { get; init; }
}

public enum HclLiteralKind { String, Number, Bool, Null }

/// Целиком интерполированное выражение: `${api_name}-${env}` или `var.foo`.
/// Хранится как RawText (с фигурными скобками или без, как было).
public sealed record HclInterpolation : HclValue
{
    /// Полный исходный текст между кавычками, включая `${...}`.
    /// Пример: `"${api_name}-${env}"` → InnerText = `${api_name}-${env}`.
    public required string InnerText { get; init; }

    /// Извлечённые имена переменных/выражений в порядке появления.
    /// Для `"${api_name}-${env}"` это [api_name, env].
    public IReadOnlyList<string> ReferencedExpressions { get; init; } = [];
}

/// `<<XML ... XML` или `<<-XML ... XML` (indented).
public sealed record HclHeredoc : HclValue
{
    public required string Marker { get; init; }     // "XML"
    public required string Content { get; init; }    // как есть, без маркеров
    public bool Indented { get; init; }              // <<- variant
}
```

**Правила сравнения**:

- Два `HclLiteral` равны при равенстве `Kind` и `RawValue` (для String — после унификации кавычек).
- Два `HclInterpolation` структурно равны при равенстве `InnerText` (после нормализации пробелов внутри `${...}`).
- `HclLiteral{String, "foo"}` и `HclInterpolation{InnerText="foo"}` **не равны** (один — literal, второй — interpolation, даже если резолвится в `foo`).

### 2.2. Распарсенная APIM-структура

Файл: `src/TerraformApi.Domain/Models/Apim/ParsedApimDocument.cs`.

```csharp
namespace TerraformApi.Domain.Models.Apim;

/// Что мы вынули из HCL поверх AST.
public sealed record ParsedApimDocument
{
    /// Исходный AST, на который ссылаются ParsedApiGroup'ы своими путями.
    public required HclDocument Ast { get; init; }

    /// Путь от корня до родителя `api_group_name` блоков
    /// (`["apis","bpc_apis","backend_apis"]` для рабочего примера).
    /// null — если структура плоская (`api_group_name = { ... }` сразу в корне).
    public IReadOnlyList<string>? ApiGroupParentPath { get; init; }

    public List<ParsedApiGroup> ApiGroups { get; init; } = [];
}

public sealed record ParsedApiGroup
{
    public required string ApiGroupName { get; init; }   // как было в HCL (с кавычками или нет)

    /// Ссылка на узел в Ast (`HclObject`), который содержит api/api_operations/product.
    public required HclObject AstNode { get; init; }

    public List<ParsedApi> Apis { get; init; } = [];
    public List<ParsedApiOperation> Operations { get; init; } = [];
}

public sealed record ParsedApi
{
    public required HclObject AstNode { get; init; }

    /// Извлечённые значения. Каждое поле — это (Raw, MaybeResolved).
    public HclValueRef Name { get; init; } = new();
    public HclValueRef ServiceUrl { get; init; } = new();
    public HclValueRef Path { get; init; } = new();
    public HclValueRef? Policy { get; init; }  // HclHeredoc если есть
    // ... остальные поля по ApimApi
}

public sealed record ParsedApiOperation
{
    public required HclObject AstNode { get; init; }

    public required HclValueRef OperationId { get; init; }
    public required HclValueRef Method { get; init; }
    public required HclValueRef UrlTemplate { get; init; }
    public HclValueRef? DisplayName { get; init; }
    public HclValueRef? StatusCode { get; init; }
    public HclValueRef? Description { get; init; }

    /// Подобъекты request / response — оставляем как сырые ссылки на AST,
    /// чтобы merge их параметров был операцией над массивами AST.
    public HclArray? RequestArray { get; init; }
    public HclArray? ResponsesArray { get; init; }
}

/// Удобная обёртка над значением в AST: даёт и сырой узел, и
/// «лучшее текстовое представление» для сравнения.
public sealed record HclValueRef
{
    public HclValue? Node { get; init; }

    /// Текст для structural-сравнения: для literal — RawValue;
    /// для interpolation — `${...}` целиком; для heredoc — Content.
    public string? StructuralText =>
        Node switch
        {
            HclLiteral l => l.RawValue,
            HclInterpolation i => i.InnerText,
            HclHeredoc h => h.Content,
            _ => null
        };
}
```

### 2.3. Идентификация и сопоставление операций

Файл: `src/TerraformApi.Domain/Models/Sync/OperationFingerprint.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

/// Композитный «отпечаток» операции для сопоставления.
/// Никакое поле не обязательно — какие заполнены, такие и участвуют
/// в сравнении (см. OperationMatchStrategy.Keys).
public sealed record OperationFingerprint
{
    public string? OperationId { get; init; }
    public string? Method { get; init; }            // нормализованный к UPPER
    public string? UrlTemplate { get; init; }       // нормализованный (см. правила ниже)
    public string? ParameterSignature { get; init; }
    public string? Tag { get; init; }
    public string? ApiName { get; init; }           // для disambiguation между API в одном group

    /// Из чего был построен (для отладки/отчёта).
    public OperationFingerprintSource SourceMarker { get; init; }
}

public enum OperationFingerprintSource
{
    OpenApi,
    ExistingTerraform,
    Resolved          // после применения TerraformInterpolationResolver
}

/// Какие поля fingerprint'а реально сравнивать и в каком приоритете.
public enum OperationMatchKey
{
    OperationId,
    MethodAndUrl,
    MethodAndUrlAndParams,
    Tag,
    ApiAndMethodAndUrl,
    Custom
}

/// Стратегия — упорядоченный список ключей.
/// Сравнение идёт сверху вниз; первый совпавший ключ → match.
public sealed record OperationMatchStrategy
{
    /// Порядок матчинга. Дефолт — самая безопасная схема для merge между средами.
    public IReadOnlyList<OperationMatchKey> Keys { get; init; } =
    [
        OperationMatchKey.MethodAndUrl,
        OperationMatchKey.OperationId,
        OperationMatchKey.Tag
    ];

    /// Нормализация URL перед сравнением.
    public UrlNormalizationOptions UrlNormalization { get; init; } = new();

    /// Кастомный matcher для OperationMatchKey.Custom.
    public Func<OperationFingerprint, OperationFingerprint, bool>? CustomMatcher { get; init; }

    /// Если включено и сравнение в structural-mode не дало match,
    /// применяем TerraformInterpolationResolver и пробуем ещё раз.
    public bool TryResolvedComparisonAsFallback { get; init; } = true;

    /// Контекст переменных для резолва (если TryResolvedComparisonAsFallback = true
    /// или strategy явно требует resolved-mode).
    public IReadOnlyDictionary<string, string>? VariableContext { get; init; }
}

public sealed record UrlNormalizationOptions
{
    public bool LowercaseScheme { get; init; } = true;
    public bool TrimTrailingSlash { get; init; } = true;
    public bool CollapseSlashes { get; init; } = true;
    public bool NormalizeBraceParams { get; init; } = true; // {id} ≡ {ID} ≡ :id

    /// Считать ли `users` и `/users` одним и тем же.
    public bool TreatLeadingSlashAsOptional { get; init; } = true;
}
```

**Правила нормализации URL** (`UrlNormalizationOptions`):

| Опция                         | Эффект                                                                                                                                                     |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LowercaseScheme`             | `HTTPS://...` → `https://...` (вряд ли встретится в template, но на всякий)                                                                                |
| `TrimTrailingSlash`           | `/users/` → `/users`                                                                                                                                       |
| `CollapseSlashes`             | `/users//{id}` → `/users/{id}`                                                                                                                             |
| `NormalizeBraceParams`        | `{userId}` → `{param}` (только для матчинга, не для записи!). Опционально, по умолчанию — НЕ нормализуем имена, только унифицируем синтаксис `{x}` vs `:x` |
| `TreatLeadingSlashAsOptional` | `users` ≡ `/users`                                                                                                                                         |

### 2.4. Политика merge

Файл: `src/TerraformApi.Domain/Models/Sync/MergePolicy.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

public enum FieldMergePolicy
{
    /// Никогда не трогаем существующее значение.
    Preserve,

    /// Записываем только если поле отсутствует или пустое
    /// (null / "" / пустой массив / пустой объект).
    EnrichIfMissing,

    /// Перезаписываем безусловно (используется только в Convert-сценарии,
    /// в Sync-сценарии запрещено для всех полей по умолчанию).
    Overwrite
}

public enum CollectionMergePolicy
{
    /// Никогда не меняем коллекцию.
    Preserve,

    /// Добавляем элементы из OpenAPI, которых нет в Terraform
    /// (по item-fingerprint). Существующие остаются нетронутыми.
    AppendMissing,

    /// AppendMissing + если элемент с тем же fingerprint существует,
    /// рекурсивно применяем enrichment к его полям.
    AppendAndEnrich,

    /// Полная замена (запрещено в Sync).
    Replace
}

public sealed record MergePolicy
{
    /// Что делать с операцией целиком, если её нет в OpenAPI, но есть в TF.
    /// Append-only ⇒ Preserve.
    public OperationPreservationMode UnknownOperationPolicy { get; init; }
        = OperationPreservationMode.Preserve;

    /// Что делать с операцией, которая есть в OpenAPI, но нет в TF.
    public NewOperationMode NewOperationPolicy { get; init; }
        = NewOperationMode.Append;

    /// Per-field политика для существующих операций.
    /// Ключ — имя поля APIM-операции (operation_id, display_name, method,
    /// url_template, status_code, description). Значение — политика.
    public IReadOnlyDictionary<string, FieldMergePolicy> OperationFieldPolicies
    { get; init; } = DefaultAppendOnlyFieldPolicies;

    /// Политика для коллекций внутри операции: request.header[],
    /// request.query[], request.template[], responses[] и т. д.
    public IReadOnlyDictionary<string, CollectionMergePolicy> CollectionPolicies
    { get; init; } = DefaultAppendOnlyCollectionPolicies;

    /// Per-field политика для блока API (display_name, service_url, policy и т. д.).
    public IReadOnlyDictionary<string, FieldMergePolicy> ApiFieldPolicies
    { get; init; } = DefaultAppendOnlyApiFieldPolicies;

    public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
        DefaultAppendOnlyFieldPolicies = new Dictionary<string, FieldMergePolicy>
    {
        ["operation_id"]  = FieldMergePolicy.Preserve,        // identity, не трогаем
        ["method"]        = FieldMergePolicy.Preserve,        // не меняем тип метода
        ["url_template"]  = FieldMergePolicy.Preserve,        // не меняем URL
        ["display_name"]  = FieldMergePolicy.EnrichIfMissing,
        ["description"]   = FieldMergePolicy.EnrichIfMissing,
        ["status_code"]   = FieldMergePolicy.EnrichIfMissing
    };

    public static readonly IReadOnlyDictionary<string, CollectionMergePolicy>
        DefaultAppendOnlyCollectionPolicies = new Dictionary<string, CollectionMergePolicy>
    {
        ["request.header"]    = CollectionMergePolicy.AppendMissing,
        ["request.query"]     = CollectionMergePolicy.AppendMissing,
        ["request.template"]  = CollectionMergePolicy.AppendMissing,
        ["responses"]         = CollectionMergePolicy.AppendMissing,
        ["responses.header"]  = CollectionMergePolicy.AppendMissing,
        ["responses.representation"] = CollectionMergePolicy.AppendMissing
    };

    public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
        DefaultAppendOnlyApiFieldPolicies = new Dictionary<string, FieldMergePolicy>
    {
        ["name"]           = FieldMergePolicy.Preserve,
        ["display_name"]   = FieldMergePolicy.Preserve,
        ["path"]           = FieldMergePolicy.Preserve,
        ["service_url"]    = FieldMergePolicy.Preserve,
        ["policy"]         = FieldMergePolicy.Preserve,
        ["protocols"]      = FieldMergePolicy.Preserve,
        ["revision"]       = FieldMergePolicy.Preserve
    };
}

public enum OperationPreservationMode
{
    /// Append-only дефолт: оставляем как есть.
    Preserve,
    /// Помечаем `deprecated` в description (не удаляем!).
    MarkDeprecated,
    /// Удаляем (для Convert from scratch).
    Remove
}

public enum NewOperationMode
{
    /// Добавляем в конец массива api_operations.
    Append,
    /// Не добавляем, только репортим.
    ReportOnly,
    /// Добавляем в специальное место (например, до маркер-комментария).
    AppendBeforeMarker
}
```

**Замечание для LLM**: дефолтная политика — это **семантика «append-only»**. Если пользователь хочет менять `display_name` при синхронизации — он передаёт `MergePolicy.WithOverride("display_name", FieldMergePolicy.EnrichIfMissing → Overwrite)`. Эта гранулярность и есть запрашиваемая «гибкость».

### 2.5. Диффы и отчёт

Файл: `src/TerraformApi.Domain/Models/Sync/SyncReport.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record OperationDiff
{
    public required OperationFingerprint TerraformFingerprint { get; init; }
    public OperationFingerprint? OpenApiFingerprint { get; init; }
    public required OperationDiffKind Kind { get; init; }
    public List<FieldDiff> FieldDiffs { get; init; } = [];
    /// Что **на самом деле было применено** (после прохода через MergePolicy).
    public List<string> AppliedChanges { get; init; } = [];
    public List<string> SkippedDueToPolicy { get; init; } = [];
}

public enum OperationDiffKind
{
    /// В обоих, разницы нет.
    Identical,
    /// В обоих, есть разница (см. FieldDiffs).
    Changed,
    /// Только в OpenAPI → будет добавлено.
    AddedFromOpenApi,
    /// Только в Terraform → сохранено как есть (append-only).
    PreservedFromTerraform,
    /// Помечено как дубликат (см. DuplicateGroups).
    Duplicate
}

public sealed record FieldDiff
{
    public required string FieldPath { get; init; }   // "operation_id", "request.header[name=Auth]"
    public required string? TerraformValue { get; init; }
    public required string? OpenApiValue { get; init; }
    public required FieldDiffOutcome Outcome { get; init; }
}

public enum FieldDiffOutcome
{
    NoChange,
    AppliedEnrichIfMissing,
    AppliedOverwrite,
    SkippedPreserve,
    AppliedCollectionAppend
}

public sealed record DuplicateGroup
{
    public required OperationMatchKey MatchedBy { get; init; }
    public required string MatchedValue { get; init; }    // e.g. "GET /users"
    public List<DuplicateMember> Members { get; init; } = [];
}

public sealed record DuplicateMember
{
    public required string OperationId { get; init; }     // как в HCL, может с ${...}
    public required string ApiGroupName { get; init; }
    public required string ApiName { get; init; }
    public required int LineInSource { get; init; }
    public DuplicateSeverity Severity { get; init; }
}

public enum DuplicateSeverity
{
    /// Один и тот же operation_id внутри одного api_group/api — критично.
    HardDuplicate,
    /// Разные operation_id, но одинаковые (method, url) в одном api — APIM отклонит.
    LogicalDuplicate,
    /// Одинаковые (method, url) в разных api → допустимо, но подозрительно.
    CrossApiSimilarity
}

public sealed record SyncReport
{
    public required DateTime GeneratedAt { get; init; }
    public required string ApiGroupName { get; init; }

    public int TotalOperationsInTerraform { get; init; }
    public int TotalOperationsInOpenApi { get; init; }

    public int OperationsAdded { get; init; }
    public int OperationsPreserved { get; init; }
    public int OperationsEnriched { get; init; }
    public int OperationsIdentical { get; init; }

    public List<OperationDiff> Diffs { get; init; } = [];
    public List<DuplicateGroup> Duplicates { get; init; } = [];

    /// Предупреждения, которые не блокируют sync, но требуют ревью.
    public List<SyncWarning> Warnings { get; init; } = [];
}

public sealed record SyncWarning
{
    public required string Message { get; init; }
    public string? OperationId { get; init; }
    public SyncWarningKind Kind { get; init; }
}

public enum SyncWarningKind
{
    OperationIdContainsInterpolation,   // operation_id = "${...}-${env}" — ок, но матчинг по structural
    UrlTemplateContainsInterpolation,
    AmbiguousMatch,                     // нашли несколько кандидатов
    SkippedFieldDueToPolicy,
    UnknownFieldInOpenApi,
    DuplicateDetected
}

public sealed record SyncResult
{
    public required bool Success { get; init; }
    public required string TerraformConfig { get; init; }   // финальный HCL
    public required SyncReport Report { get; init; }
    public OperationExecutionGraph? ExecutionGraph { get; init; }
    public List<string> Errors { get; init; } = [];
}
```

---

## 3. Интерфейсы

### 3.1. `IHclParser`

Файл: `src/TerraformApi.Domain/Interfaces/IHclParser.cs`.

```csharp
public interface IHclParser
{
    /// Парсит HCL-источник в AST.
    /// Бросает HclParseException с указанием Line/Column при синтаксических ошибках.
    HclDocument Parse(string source);

    /// Best-effort парсинг: не бросает исключения, возвращает ParseDiagnostic[].
    HclParseResult TryParse(string source);
}

public sealed record HclParseResult
{
    public HclDocument? Document { get; init; }
    public List<HclParseDiagnostic> Diagnostics { get; init; } = [];
    public bool IsSuccess => Document is not null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
```

### 3.2. `IHclWriter`

```csharp
public interface IHclWriter
{
    string Write(HclDocument document, HclWriteOptions? options = null);
}

public sealed record HclWriteOptions
{
    public int IndentSize { get; init; } = 2;
    public bool AlignAssignmentEquals { get; init; } = true; // как в примере: длинные ключи в одну колонку
    public int MaxAlignedKeyLength { get; init; } = 36;
    public string LineEnding { get; init; } = "\n";
    public bool PreserveOriginalFormatting { get; init; } = true; // переиспользует исходные heredocs/строки
}
```

### 3.3. `IApimTerraformReader`

```csharp
public interface IApimTerraformReader
{
    ParsedApimDocument Read(string terraformSource);
    ParsedApimDocument Read(HclDocument document);

    /// Возможные структурные шаблоны, которые reader понимает.
    /// Каждый — это путь от корня до родителя `<api_group_name>` блоков.
    IReadOnlyList<IReadOnlyList<string>> KnownApiGroupPaths { get; }
}
```

**Замечание**: reader пробует пути в порядке `KnownApiGroupPaths`. Дефолты:

1. `["apis", "bpc_apis", "backend_apis"]` (как в примере)
2. `["apis", "backend_apis"]`
3. `[]` (плоский: `api_group = {...}` сразу на верхнем уровне)

Дополнительный путь можно передать через `ApimReaderOptions.CustomPaths`.

### 3.4. `IApimTerraformWriter`

```csharp
public interface IApimTerraformWriter
{
    /// Записывает после модификаций AST. Опции наследует от HclWriter.
    string Write(ParsedApimDocument parsed, HclWriteOptions? options = null);

    /// Помощник: построить ParsedApimDocument с нуля из ApimConfiguration
    /// (используется в Convert-сценарии).
    ParsedApimDocument BuildFromConfiguration(
        ApimConfiguration configuration,
        IReadOnlyList<string>? apiGroupParentPath = null);
}
```

### 3.5. `IOperationMatcher`

```csharp
public interface IOperationMatcher
{
    /// Создаёт fingerprint из ApimApiOperation (OpenAPI side).
    OperationFingerprint FingerprintFromOpenApi(
        ApimApiOperation operation,
        OperationMatchStrategy strategy);

    /// Создаёт fingerprint из ParsedApiOperation (Terraform side).
    OperationFingerprint FingerprintFromTerraform(
        ParsedApiOperation operation,
        OperationMatchStrategy strategy);

    /// Сопоставляет наборы. Возвращает три партиции + список неоднозначностей.
    MatchResult Match(
        IReadOnlyList<OperationFingerprint> openApiFingerprints,
        IReadOnlyList<OperationFingerprint> terraformFingerprints,
        OperationMatchStrategy strategy);
}

public sealed record MatchResult
{
    /// Операции из OpenAPI, для которых не нашлось пары в Terraform.
    public List<OperationFingerprint> OnlyInOpenApi { get; init; } = [];

    /// Операции из Terraform, для которых не нашлось пары в OpenAPI.
    public List<OperationFingerprint> OnlyInTerraform { get; init; } = [];

    /// Пары "Terraform ↔ OpenAPI". Левая часть — TF, правая — OpenAPI.
    public List<(OperationFingerprint Tf, OperationFingerprint OpenApi)> Matched { get; init; } = [];

    /// Случаи, когда один TF-fingerprint совпал с несколькими OpenAPI или наоборот.
    public List<AmbiguousMatch> Ambiguities { get; init; } = [];
}

public sealed record AmbiguousMatch
{
    public required OperationFingerprint Source { get; init; }
    public required IReadOnlyList<OperationFingerprint> Candidates { get; init; }
    public required OperationMatchKey AmbiguousOnKey { get; init; }
}
```

### 3.6. `IDuplicateDetector`

```csharp
public interface IDuplicateDetector
{
    /// Запускает все ключи матчинга на одном наборе (внутри HCL).
    /// Возвращает группы, где >1 операции имеют одинаковый ключ.
    List<DuplicateGroup> Detect(
        ParsedApimDocument parsed,
        OperationMatchStrategy strategy);
}
```

### 3.7. `IAppendOnlySynchronizer`

```csharp
public interface IAppendOnlySynchronizer
{
    SyncResult Synchronize(
        ParsedApimDocument existingParsed,
        ApimConfiguration newConfiguration,
        MergePolicy policy,
        OperationMatchStrategy matchStrategy);
}
```

---

## 4. Алгоритмы (псевдокод)

### 4.1. HCL Lexer

**Назначение**: разбивает источник на токены.

**Типы токенов**:

```
LBRACE      {
LBRACE      }       (тот же тип, но Кind=Close)
LBRACKET    [
RBRACKET    ]
EQUALS      =
COMMA       ,
IDENT       [a-zA-Z_][a-zA-Z0-9_-]*
STRING      "..." (с поддержкой \" и ${...})
NUMBER      \d+(\.\d+)?
HEREDOC_START << или <<-, затем IDENT
HEREDOC_END  IDENT в начале строки, совпадающий со start
NEWLINE     \n (значим только внутри heredoc и для column tracking)
COMMENT     # ... \n   или   // ... \n   или   /* ... */
EOF
```

**Псевдокод**:

```
read source char-by-char
maintain Line, Column

loop:
  skip whitespace/comments (except newlines inside heredoc)
  ch = peek()
  case ch:
    '{', '}' → emit LBRACE/RBRACE, advance
    '[', ']' → emit LBRACKET/RBRACKET, advance
    '=' → emit EQUALS, advance
    ',' → emit COMMA, advance
    '"' → read_string() (см. ниже)
    '<' + '<' → read_heredoc_start_then_body()
    digit → read_number()
    letter or '_' → read_ident()
    other → ParseError

read_string():
  advance past "
  buffer = ""
  while peek() != '"':
    if peek() == '\\':
      advance, append next char as escape
    elif peek() == '$' and peek_next() == '{':
      # это часть интерполяции; читаем до парной }
      read до '}' с учётом вложенных { }
    else:
      append peek(), advance
  advance past "
  emit STRING(buffer, hadInterpolation: bool)

read_heredoc_body(marker):
  # после <<MARKER\n идёт content до строки, точно равной MARKER (или после <<-, с trim leading whitespace)
  collect lines until a line that, trimmed, equals marker
  emit HEREDOC(marker, content, indented)
```

**Edge cases**:

- `${...}` внутри строки: парсер строки должен распознать, что строка содержит интерполяцию. Один STRING-токен с флагом `HasInterpolation = true`. Точное разделение на «литеральные куски» и «выражения» делается уже при создании `HclInterpolation`.
- Вложенные `{` `}` внутри `${...}` — нужен баланс скобок.
- Heredoc indented `<<-XML`: при чтении трим минимальный отступ всех строк.
- Комментарии `#` и `//` эквивалентны. Многострочные `/* */` для полноты.

### 4.2. HCL Parser

**Грамматика (упрощённая)**:

```
Document      := Assignment*
Assignment    := Key '=' Value
Key           := IDENT | STRING_LITERAL_QUOTED
Value         := Object | Array | Literal | Interpolation | Heredoc
Object        := '{' Assignment* '}'
Array         := '[' (Value (',' Value)* ','?)? ']'
Literal       := NUMBER | BOOL | NULL | STRING_NO_INTERP
Interpolation := STRING_WITH_INTERP
Heredoc       := HEREDOC token
```

**Рекурсивный спуск**, без backtrack. На каждой ошибке — `HclParseException(line, col, expected, found)`.

**Сохранение позиций**: каждый узел AST хранит Line/Column своего первого токена.

### 4.3. ApimTerraformReader

**Алгоритм**:

```
1. Parse(source) → HclDocument ast
2. Для каждого known path:
   a. Навигируем от корня по path (находим HclObject)
   b. Если найден → перебираем его HclAssignment'ы — каждый ключ это api_group_name
   c. Для каждого api_group: ищем HclAssignment "api" (массив) и "api_operations" (массив)
3. Если ни один known path не сработал, пробуем path = [] (плоский корень):
   ищем HclAssignment'ы верхнего уровня, у которых value — HclObject с полями api/api_operations.
4. Для каждой найденной операции (HclObject):
   a. Извлекаем required-поля operation_id, method, url_template
   b. Извлекаем optional поля
   c. Сохраняем ссылку на HclObject (это будущая точка модификации)
5. Возвращаем ParsedApimDocument с заполненной коллекцией ApiGroups.
```

**Защита от ложных срабатываний**: внутри policy heredoc (`<<XML ... XML`) встречается XML с тегами `<method>GET</method>`. Это **не** должно попасть в извлечённые операции, потому что heredoc на уровне AST — это один `HclHeredoc`-узел, а не объект с ключами. Reader работает только с `HclObject`. Это автоматически решает проблему, на которой сейчас падает regex-подход.

### 4.4. OperationMatcher

**Алгоритм Match**:

```
function Match(openApiList, tfList, strategy):
    matched = []
    ambiguities = []

    for each key in strategy.Keys:
        # строим обратный индекс по этому ключу для оставшихся TF
        tfIndex = group_by(remaining_tf, key)
        # для каждого OpenAPI fingerprint ищем совпадение
        for op in remaining_openapi:
            candidates = tfIndex[op.key_value(key)]
            if len(candidates) == 1:
                matched.append((candidates[0], op))
                remove candidates[0] from remaining_tf
                remove op from remaining_openapi
            elif len(candidates) > 1:
                ambiguities.append(AmbiguousMatch(op, candidates, key))
            else:
                pass  # пробуем следующий key

        if remaining_openapi empty: break

    if strategy.TryResolvedComparisonAsFallback and remaining_openapi non-empty:
        # Перестраиваем fingerprints с резолвом и повторяем цикл
        resolved_openapi = [resolve(o) for o in remaining_openapi]
        resolved_tf = [resolve(t) for t in remaining_tf]
        # ... тот же цикл

    onlyInOpenApi = remaining_openapi
    onlyInTerraform = remaining_tf
    return MatchResult(matched, onlyInOpenApi, onlyInTerraform, ambiguities)
```

**Извлечение значений ключа из fingerprint'а**:

```
key_value(fingerprint, key):
    case key:
        OperationId           → fingerprint.OperationId
        MethodAndUrl          → fingerprint.Method + "|" + Normalize(fingerprint.UrlTemplate)
        MethodAndUrlAndParams → ... + "|" + fingerprint.ParameterSignature
        Tag                   → fingerprint.Tag
        ApiAndMethodAndUrl    → fingerprint.ApiName + "|" + Method + "|" + Url
        Custom                → strategy.CustomMatcher(...)
    return null if любое требуемое поле null
```

**Построение ParameterSignature** из ApimApiOperation:

```
function ParameterSignature(operation):
    parts = []
    for request in operation.Requests:
        for header in request.Headers:
            parts.append("h:" + header.Name)
        for query in request.QueryParameters:
            parts.append("q:" + query.Name)
        for template in request.TemplateParameters:
            parts.append("t:" + template.Name)
    parts.sort()
    return "|".join(parts)
```

Имена параметров сравниваем case-insensitive. Тип (string/int) — не включаем в подпись по умолчанию (опционально через `MatchStrategy.IncludeParameterTypesInSignature`).

### 4.5. DuplicateDetector

```
function Detect(parsed, strategy):
    groups = []
    for each key in [OperationId, MethodAndUrl, ApiAndMethodAndUrl]:
        index = group_by(allOperations, key)
        for value, members in index:
            if len(members) > 1:
                severity = determine_severity(key, members)
                groups.append(DuplicateGroup(key, value, members, severity))
    # Также: для MethodAndUrlAndParams (если параметры совпадают полностью — самый strict дубликат)
    return groups

function determine_severity(key, members):
    if key == OperationId and same_api_group(members):
        return HardDuplicate
    if key == MethodAndUrl and same_api(members):
        return LogicalDuplicate
    if key == MethodAndUrl and different_api(members):
        return CrossApiSimilarity
    return Info
```

### 4.6. AppendOnlySynchronizer (главный алгоритм)

```
function Synchronize(existingParsed, newConfig, policy, strategy):
    report = new SyncReport
    duplicates = duplicateDetector.Detect(existingParsed, strategy)
    report.Duplicates = duplicates

    # Найти ApiGroup в existingParsed по имени из newConfig.
    # Если нет — создаём новую (это первый sync для этой группы).
    targetGroup = existingParsed.ApiGroups.find(g => g.ApiGroupName matches newConfig.ApiGroupName)
    if targetGroup is null:
        targetGroup = appendNewApiGroup(existingParsed, newConfig)
        # все операции пойдут в Added

    tfFingerprints = targetGroup.Operations.Select(o => matcher.FingerprintFromTerraform(o, strategy))
    openApiFingerprints = newConfig.ApiOperations.Select(o => matcher.FingerprintFromOpenApi(o, strategy))

    matchResult = matcher.Match(openApiFingerprints, tfFingerprints, strategy)

    # --- 1. Операции, которые есть только в OpenAPI: добавляем ---
    if policy.NewOperationPolicy == Append:
        for openApiOp in matchResult.OnlyInOpenApi:
            newAstNode = buildHclObjectForOperation(openApiOp.SourceModel, naming context)
            appendToArray(targetGroup.AstNode.Get("api_operations") as HclArray, newAstNode)
            report.Diffs.Add(OperationDiff{ Kind=AddedFromOpenApi, ... })
            report.OperationsAdded++

    # --- 2. Операции, которые есть только в Terraform: оставляем ---
    for tfOp in matchResult.OnlyInTerraform:
        # По дефолту UnknownOperationPolicy=Preserve — ничего не делаем с AST.
        # Просто фиксируем в репорте.
        report.Diffs.Add(OperationDiff{ Kind=PreservedFromTerraform, ... })
        report.OperationsPreserved++

    # --- 3. Совпавшие: enrichment по политике ---
    for (tfFp, openApiFp) in matchResult.Matched:
        tfOp = find_parsed_op(tfFp)
        openApiOp = find_openapi_op(openApiFp)
        diff = computeDiff(tfOp, openApiOp)

        applied = []
        skipped = []
        for fieldDiff in diff.FieldDiffs:
            policy_for_field = resolvePolicy(fieldDiff.FieldPath, policy)
            case policy_for_field:
                Preserve:
                    skipped.append(fieldDiff.FieldPath)
                    fieldDiff.Outcome = SkippedPreserve
                EnrichIfMissing:
                    if isMissing(tfOp, fieldDiff.FieldPath):
                        writeIntoAst(tfOp.AstNode, fieldDiff.FieldPath, fieldDiff.OpenApiValue)
                        applied.append(fieldDiff.FieldPath)
                        fieldDiff.Outcome = AppliedEnrichIfMissing
                    else:
                        skipped.append(fieldDiff.FieldPath)
                        fieldDiff.Outcome = SkippedPreserve
                Overwrite:
                    writeIntoAst(...)
                    applied.append(...)

        # Коллекции (request.header, responses, ...)
        for collectionPath, items in diff.CollectionDiffs:
            collPolicy = resolveCollectionPolicy(collectionPath, policy)
            case collPolicy:
                Preserve: skip
                AppendMissing:
                    for item in items.OnlyInOpenApi:
                        appendToArray(tfOp.AstNode.descendant(collectionPath), buildHclObject(item))
                        applied.append(collectionPath + " += " + item.Name)

        diffKind = applied.Empty ? Identical : Changed
        report.Diffs.Add(OperationDiff{ Kind=diffKind, FieldDiffs=diff.FieldDiffs,
                                         AppliedChanges=applied, SkippedDueToPolicy=skipped })
        if diffKind == Changed: report.OperationsEnriched++
        else: report.OperationsIdentical++

    # Финальный HCL
    finalHcl = writer.Write(existingParsed.Ast, options)
    return SyncResult(true, finalHcl, report, executionGraph, errors=[])
```

### 4.7. HclWriter

**Принципы**:

1. **Не теряем форматирование** уже существующих узлов, если их не трогали (а это 95% документа). Достигается тем, что unchanged-узлы хранят `OriginalSource` (slice исходника по Line/Column), и writer вставляет именно эту строку.
2. **Новые узлы** генерируем канонично: 2 пробела отступа, выравнивание `=` по самому длинному ключу (как в примере `apim_resource_group_name         = "..."`).
3. **Heredoc'и** пишутся обратно как есть (`<<XML ... XML`).
4. **Интерполяции** оборачиваются в кавычки: `"${api_name}-${env}"`.

**Псевдокод**:

```
function Write(document, options):
    sb = StringBuilder
    for assignment in document.RootAssignments:
        writeAssignment(sb, assignment, indent=0)
    return sb.toString()

function writeAssignment(sb, a, indent):
    if a.OriginalSource and a not modified: # быстрый путь
        sb.append(a.OriginalSource); return
    keyText = a.KeyIsQuoted ? "\"" + a.Key + "\"" : a.Key
    sb.append(spaces(indent) + keyText + " = ")
    writeValue(sb, a.Value, indent)
    sb.append("\n")

function writeValue(sb, v, indent):
    case v:
        HclLiteral(String, raw):  sb.append("\"" + escape(raw) + "\"")
        HclLiteral(Other, raw):   sb.append(raw)
        HclInterpolation(text):   sb.append("\"" + text + "\"")
        HclHeredoc(marker, body, indented):
            sb.append(indented ? "<<-" : "<<")
            sb.append(marker + "\n" + body + "\n" + marker)
        HclObject(assignments):
            sb.append("{\n")
            keyWidth = maxKeyLength(assignments)
            for a in assignments:
                sb.append(spaces(indent+2))
                sb.append(padRight(a.Key, keyWidth) + " = ")
                writeValue(sb, a.Value, indent+2)
                sb.append("\n")
            sb.append(spaces(indent) + "}")
        HclArray(items):
            sb.append("[\n")
            for i, item in enumerate(items):
                sb.append(spaces(indent+2))
                writeValue(sb, item, indent+2)
                if i < len(items) - 1: sb.append(",")
                sb.append("\n")
            sb.append(spaces(indent) + "]")
```

### 4.8. TerraformInterpolationResolver

```csharp
public sealed class TerraformInterpolationResolver
{
    public string Resolve(string template, IReadOnlyDictionary<string, string> variables)
    {
        // template = "${api_name}-${env}"
        // variables = { api_name: "bpc", env: "dev" }
        // result = "bpc-dev"

        // Реализация: regex \$\{([^}]+)\}, для каждой группы:
        //   - если variables содержит ключ → подставляем
        //   - иначе оставляем ${...} как есть
        // Возвращаем результат с warning, если остались нерезолвленные ${...}.
    }

    public ResolveResult ResolveWithReport(string template, IReadOnlyDictionary<string, string> variables);
}

public sealed record ResolveResult
{
    public required string Value { get; init; }
    public List<string> UnresolvedExpressions { get; init; } = [];
    public bool HasUnresolvedExpressions => UnresolvedExpressions.Any();
}
```

**Важно**: resolver обрабатывает только простые `${var_name}` и `${var.path}`. Сложные выражения Terraform (функции, тернарники) **не** поддерживаются — оставляются как есть и репортятся в `UnresolvedExpressions`.

---

## 5. Edge cases — обязательный чеклист для тестов

LLM реализующая это **должна написать тест на каждый пункт ниже**, иначе фича не считается готовой.

### 5.1. Парсер HCL

| #   | Случай                                                          | Ожидание                                                                  |
| --- | --------------------------------------------------------------- | ------------------------------------------------------------------------- |
| P1  | Пустой документ                                                 | `HclDocument` с пустым `RootAssignments`, без ошибок                      |
| P2  | Только комментарии                                              | То же                                                                     |
| P3  | Простое присваивание `a = "b"`                                  | Один assignment с `HclLiteral{String,"b"}`                                |
| P4  | `a = "${x}"`                                                    | `HclInterpolation{InnerText="${x}"}`, `ReferencedExpressions=["x"]`       |
| P5  | `a = "prefix-${x}-suffix"`                                      | Тот же тип; `InnerText` сохраняет всё; `ReferencedExpressions=["x"]`      |
| P6  | `a = "${x}-${y}"`                                               | `ReferencedExpressions=["x","y"]`                                         |
| P7  | Heredoc `a = <<XML\n<foo/>\nXML`                                | `HclHeredoc{marker="XML", content="<foo/>", indented=false}`              |
| P8  | Indented heredoc `<<-XML` с табами/пробелами в начале           | Минимальный отступ обрезан, ровно как в TF spec                           |
| P9  | Массив объектов с trailing comma                                | Парсится корректно                                                        |
| P10 | Глубоко вложенная структура из примера пользователя (5 уровней) | Корректный AST, ApiGroupParentPath = `["apis","bpc_apis","backend_apis"]` |
| P11 | Ключ в кавычках: `"${api_group_name}" = { ... }`                | `HclAssignment{Key="${api_group_name}", KeyIsQuoted=true}`                |
| P12 | Невалидный HCL (несбалансированные скобки)                      | `HclParseException` с правильной Line/Column                              |
| P13 | Числа: `port = 8080`, `ratio = 0.5`                             | `HclLiteral{Number}`                                                      |
| P14 | Bool/null: `flag = true`, `value = null`                        | Корректные типы                                                           |
| P15 | Строка с экранированной кавычкой: `a = "say \"hi\""`            | Корректный raw value                                                      |
| P16 | XML внутри heredoc, содержащий `=` и `{` `}`                    | Не должен интерпретироваться как HCL                                      |
| P17 | Многобайтовые UTF-8 символы                                     | Корректно работает                                                        |

### 5.2. Writer (round-trip)

| #   | Случай                                                    | Ожидание                                                 |
| --- | --------------------------------------------------------- | -------------------------------------------------------- |
| W1  | Парсим пример пользователя → пишем обратно → парсим снова | Два AST структурно равны                                 |
| W2  | Heredoc сохраняется байт-в-байт                           | Содержимое heredoc без модификаций                       |
| W3  | Интерполяции сохраняются                                  | `"${a}-${b}"` остаётся `"${a}-${b}"`                     |
| W4  | Выравнивание `=` для длинных ключей в новых блоках        | `apim_resource_group_name = ...` — пробелы как в примере |
| W5  | Trailing comma в массивах сохраняется/добавляется         | Согласовано с опциями                                    |

### 5.3. Reader (распознавание APIM-структуры)

| #   | Случай                                                         | Ожидание                                         |
| --- | -------------------------------------------------------------- | ------------------------------------------------ |
| R1  | Плоский корень: `my-api = { api=[...], api_operations=[...] }` | Один ApiGroup                                    |
| R2  | Структура `apis.bpc_apis.backend_apis."${api_group_name}"`     | ApiGroupParentPath проставлен                    |
| R3  | Несколько `api_group` под одним родителем                      | Все распознаны                                   |
| R4  | `api_operations` отсутствует (только `api`)                    | ApiGroup есть, Operations пустой                 |
| R5  | `api` отсутствует (только `api_operations`)                    | ApiGroup есть, Apis пустой                       |
| R6  | Поле `policy` с heredoc, содержащим `<method>POST</method>`    | `<method>` из XML НЕ должно попасть в Operations |

### 5.4. Matcher

| #   | Случай                                                                                                                        | Ожидание                                                                                                                                                                     |
| --- | ----------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| M1  | OpenAPI op `GET /users`, TF op `GET /users` (тот же operationId)                                                              | Matched                                                                                                                                                                      |
| M2  | OpenAPI `GET /users` (operationId=`listUsers`), TF `GET /users` (operationId=`${prefix}-list-${env}`) — match по MethodAndUrl | Matched                                                                                                                                                                      |
| M3  | OpenAPI `GET /users`, TF `GET /Users` (разный регистр)                                                                        | Опции `NormalizeBraceParams=false`, `LowercaseScheme=true` — не матч (case в path matters), но при специальной настройке — матч                                              |
| M4  | OpenAPI `GET /users/{id}`, TF `GET /users/{userId}`                                                                           | Без `NormalizeBraceParams` — не матч; с — матч                                                                                                                               |
| M5  | Два OpenAPI операции с одинаковым `(method, url)`, разные query параметры                                                     | Если стратегия `MethodAndUrlAndParams` — разные fingerprint'ы; иначе — `Ambiguity`                                                                                           |
| M6  | TF `url_template = "${operation_path}"` (полностью интерполированный)                                                         | Structural-fingerprint = `${operation_path}`. Матч с OpenAPI возможен только если: (а) OpenAPI тоже даёт `${operation_path}` (вряд ли), или (б) resolved-mode с подстановкой |
| M7  | OpenAPI 3 операций, TF 3 операций, всё совпадает                                                                              | 3 matched, 0 в OnlyInOpenApi/OnlyInTerraform                                                                                                                                 |
| M8  | OpenAPI пустой, TF с операциями                                                                                               | Все TF → OnlyInTerraform; SyncReport.OperationsPreserved == count                                                                                                            |
| M9  | TF пустой, OpenAPI с операциями                                                                                               | Все OpenAPI → OnlyInOpenApi; OperationsAdded == count                                                                                                                        |

### 5.5. DuplicateDetector

| #   | Случай                                                           | Ожидание                                                                            |
| --- | ---------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| D1  | Два operation_id "x" в одном api_group                           | HardDuplicate                                                                       |
| D2  | Два разных operation_id, одинаковый `(method, url)` в одном api  | LogicalDuplicate                                                                    |
| D3  | Два разных operation_id, одинаковый `(method, url)` в разных api | CrossApiSimilarity                                                                  |
| D4  | Уникальные operation_id и `(method, url)`                        | Группа дубликатов пуста                                                             |
| D5  | Дубликаты, содержащие интерполяции (`${env}`)                    | Сравнение в structural-mode → две `"${prefix}-list-${env}"` записи будут дубликатом |

### 5.6. AppendOnlySynchronizer

| #   | Случай                                                                                                        | Ожидание                                                               |
| --- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| S1  | TF с 5 операциями + OpenAPI с 5 теми же → identical                                                           | 0 изменений в HCL, SyncReport.OperationsIdentical=5                    |
| S2  | TF с 5, OpenAPI добавляет 2 новые → Added=2                                                                   | В HCL появились 2 новые записи в конце `api_operations`                |
| S3  | TF с 5, OpenAPI удалил 2 → Preserved=2 для удалённых                                                          | HCL без изменений; SyncReport.OperationsPreserved=2                    |
| S4  | TF op без `description`, OpenAPI даёт description → EnrichIfMissing                                           | В TF появилось `description = "..."`                                   |
| S5  | TF op c description="X", OpenAPI с description="Y", policy=Preserve                                           | TF не меняется                                                         |
| S6  | TF op без `request` блока, OpenAPI даёт параметры → CollectionPolicy=AppendMissing                            | В TF создаётся блок `request = [{ header = [...] }]`                   |
| S7  | TF op с `request.header[name=Authorization]`, OpenAPI добавляет `header[name=X-Trace]` → AppendMissing        | В TF добавляется новый header, существующий не трогается               |
| S8  | TF op с `url_template="/users/{id}"`, OpenAPI с `url_template="/v2/users/{id}"`, policy=Preserve по умолчанию | TF не меняется; SyncReport содержит SkippedDueToPolicy `url_template`  |
| S9  | Передан строгий policy.WithOverride("url_template", Overwrite)                                                | TF меняется; SyncReport.AppliedChanges содержит `url_template`         |
| S10 | Несколько ApiGroup в TF, OpenAPI работает только с одним → остальные не трогаются                             | Другие ApiGroup байт-в-байт не меняются (проверяется через round-trip) |
| S11 | ApiGroup из OpenAPI отсутствует в TF → создаётся новый ApiGroup                                               | Добавлен новый узел под правильным parent path                         |
| S12 | OpenAPI приносит дубликат по `(method, url)` к существующей TF-операции (но с другим operationId) → Ambiguity | SyncReport.Warnings содержит AmbiguousMatch; ничего не добавляется     |

### 5.7. Интеграционные сценарии

| #   | Сценарий                                                                                                                    | Что проверить                                                                                                                                                            |
| --- | --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| I1  | Сценарий пользователя 1 (Convert from scratch) — пустой existingTerraform, OpenAPI с 10 ops                                 | На выходе валидный HCL, все 10 операций, ExecutionGraph есть, Statistics корректные                                                                                      |
| I2  | Сценарий пользователя 2 (Sync) — рабочий пример пользователя + новый OpenAPI с 1 новой операцией и 1 изменённым description | На выходе HCL = исходный + 1 новая операция в api_operations + description заполнен у одной существующей операции; ничего не удалено; round-trip других блоков идентичен |
| I3  | Большой реальный HCL (50+ операций, 5+ api_group) + OpenAPI с пересекающимся подмножеством                                  | Производительность < 1 сек; корректность матчинга                                                                                                                        |

---

## 6. Фазированный план реализации (для Opus)

> **Каждая фаза = отдельный PR**. После каждой — `dotnet build` и зелёные тесты.

### Phase 0 — Подготовка (15 мин)

- [ ] Создать ветку `feature/apim-sync-engine`
- [ ] Зафиксировать baseline: `dotnet test` → все 274 теста зелёные
- [ ] Создать пустые папки:
  - `src/TerraformApi.Domain/Models/Hcl/`
  - `src/TerraformApi.Domain/Models/Sync/`
  - `src/TerraformApi.Domain/Models/Apim/`
  - `src/TerraformApi.Application/Services/Hcl/`
  - `src/TerraformApi.Application/Services/Apim/`
  - `src/TerraformApi.Application/Services/Sync/`
  - `tests/TerraformApi.Application.Tests/Hcl/`
  - `tests/TerraformApi.Application.Tests/Sync/`
- [ ] Положить рабочий пример пользователя в `tests/TerraformApi.Application.Tests/Fixtures/example-existing.tf` (как есть, без модификаций)

**Acceptance**: ветка готова, fixture лежит, тесты зелёные.

### Phase 1 — HCL AST + Parser + Writer

**Файлы**:

- `Domain/Models/Hcl/*.cs` (все модели AST, см. §2.1)
- `Domain/Interfaces/IHclParser.cs`, `IHclWriter.cs`
- `Application/Services/Hcl/HclLexer.cs`
- `Application/Services/Hcl/HclParserService.cs` (implements `IHclParser`)
- `Application/Services/Hcl/HclWriterService.cs` (implements `IHclWriter`)
- `Application/DependencyInjection.cs` — регистрация

**Тесты** (`tests/TerraformApi.Application.Tests/Hcl/`):

- `HclLexerTests.cs` — 15+ тестов (все токены, edge cases с heredoc, интерполяциями, экранированием)
- `HclParserTests.cs` — все P1–P17 из §5.1
- `HclWriterTests.cs` — все W1–W5 из §5.2
- **`HclRoundTripTests.cs`** — критичный тест: парсим `example-existing.tf` → пишем обратно → парсим снова → AST'ы равны. Этот тест блокирует мердж фазы.

**Acceptance**:

- Все тесты фазы зелёные
- `HclRoundTripTests.RoundTripPreservesExistingExample` проходит
- Регрессионные тесты предыдущих фаз зелёные

### Phase 2 — Доменные модели Sync

**Файлы**:

- `Domain/Models/Apim/ParsedApimDocument.cs`, `ParsedApiGroup.cs`, `ParsedApi.cs`, `ParsedApiOperation.cs`, `HclValueRef.cs`
- `Domain/Models/Sync/OperationFingerprint.cs`
- `Domain/Models/Sync/OperationMatchKey.cs` (enum)
- `Domain/Models/Sync/OperationMatchStrategy.cs`
- `Domain/Models/Sync/UrlNormalizationOptions.cs`
- `Domain/Models/Sync/FieldMergePolicy.cs` (enum)
- `Domain/Models/Sync/CollectionMergePolicy.cs` (enum)
- `Domain/Models/Sync/MergePolicy.cs`
- `Domain/Models/Sync/OperationDiff.cs`, `FieldDiff.cs`
- `Domain/Models/Sync/DuplicateGroup.cs`
- `Domain/Models/Sync/SyncReport.cs`, `SyncResult.cs`, `SyncWarning.cs`

**Тестов на этой фазе нет** (это чистые records). Но: проверка компиляции, проверка defaults — простой тест на `MergePolicy.DefaultAppendOnlyFieldPolicies` (что `operation_id`, `method`, `url_template` = Preserve).

**Acceptance**: компиляция, дефолты корректны.

### Phase 3 — ApimTerraformReader + Writer

**Файлы**:

- `Domain/Interfaces/IApimTerraformReader.cs`, `IApimTerraformWriter.cs`
- `Application/Services/Apim/ApimTerraformReaderService.cs`
- `Application/Services/Apim/ApimTerraformWriterService.cs`
- `Application/DependencyInjection.cs` — регистрация

**Алгоритм Reader** (см. §4.3).

**Алгоритм Writer**:

- `Write(parsed)` → просто делегирует `IHclWriter.Write(parsed.Ast, options)` (writer работает над AST, который уже модифицирован synchronizer'ом).
- `BuildFromConfiguration(config, parentPath)` — конструирует AST с нуля:
  - Создаёт цепочку `HclObject`'ов по `parentPath`
  - На дне создаёт `HclAssignment{Key=apiGroupName, KeyIsQuoted=true if name has ${...}}`
  - Value — `HclObject` с тремя assignments: `product=[]`, `api=[...]`, `api_operations=[...]`
  - Каждая `ApimApiOperation` → `HclObject` с полями

**Тесты** (`tests/TerraformApi.Application.Tests/Apim/`):

- R1–R6 из §5.3
- `BuildFromConfiguration_ProducesValidStructure`
- `BuildFromConfiguration_WithCustomParentPath_GeneratesNestedStructure`

**Acceptance**: reader корректно вынимает операции из `example-existing.tf` (написать тест, который ловит **точное** число операций), не путает `<method>` внутри policy с полем `method`.

### Phase 4 — TerraformInterpolationResolver

**Файлы**:

- `Application/Services/Sync/TerraformInterpolationResolver.cs`
- `Domain/Models/Sync/ResolveResult.cs`

**Тесты**:

- `Resolve_SimpleVariable_ReturnsValue` — `${env}` + `{env: dev}` = `dev`
- `Resolve_MultipleVariables` — `${a}-${b}` + `{a:1, b:2}` = `1-2`
- `Resolve_MissingVariable_LeftAsIs` — `${unknown}` + `{}` = `${unknown}`, `UnresolvedExpressions=["unknown"]`
- `Resolve_VarDotPath_Supported` — `${var.foo}` + `{var.foo: bar}` = `bar`
- `Resolve_NoInterpolation_PassThrough` — `"plain"` = `"plain"`
- `Resolve_ComplexExpression_LeftAsIs` — `${var.x ? "a" : "b"}` → unresolved

**Acceptance**: все тесты зелёные.

### Phase 5 — OperationMatcher

**Файлы**:

- `Domain/Interfaces/IOperationMatcher.cs`
- `Application/Services/Sync/OperationMatcherService.cs`

**Тесты**: M1–M9 из §5.4.

**Acceptance**: все тесты зелёные. Особое внимание M6 (полностью интерполированный URL) — тест должен проверить, что без resolved-mode операция остаётся в OnlyInTerraform, а с resolved-mode — матчится.

### Phase 6 — DuplicateDetector

**Файлы**:

- `Domain/Interfaces/IDuplicateDetector.cs`
- `Application/Services/Sync/DuplicateDetectorService.cs`

**Тесты**: D1–D5 из §5.5 + тест на `example-existing.tf` (там сейчас нет дубликатов, проверить, что детектор возвращает пустой список).

### Phase 7 — AppendOnlySynchronizer

Это самая большая фаза.

**Файлы**:

- `Domain/Interfaces/IAppendOnlySynchronizer.cs`
- `Application/Services/Sync/AppendOnlySynchronizerService.cs`

**Структура сервиса**:

```csharp
public sealed class AppendOnlySynchronizerService : IAppendOnlySynchronizer
{
    private readonly IOperationMatcher _matcher;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly IApimTerraformWriter _writer;
    private readonly IApimTerraformReader _reader;
    private readonly TerraformInterpolationResolver _resolver;
    // logger

    public SyncResult Synchronize(...)
    {
        // см. §4.6
    }

    // Приватные методы:
    private OperationDiff ComputeOperationDiff(ParsedApiOperation tf, ApimApiOperation openApi);
    private void ApplyFieldEnrichment(HclObject astNode, string fieldPath, string newValue);
    private void AppendOperationToArray(HclArray operationsArray, ApimApiOperation op, HclWriteContext ctx);
    private HclObject BuildOperationHclObject(ApimApiOperation op);
    private bool IsFieldMissing(HclObject astNode, string fieldPath);
    private FieldMergePolicy ResolvePolicy(string fieldPath, MergePolicy policy);
    private CollectionMergePolicy ResolveCollectionPolicy(string path, MergePolicy policy);
}
```

**Тесты**: S1–S12 из §5.6. Также:

- `Synchronize_AppendOnlyDefaults_NeverModifiesPreserveFields`
- `Synchronize_WithCustomPolicy_AllowsOverwriteWhenSpecified`
- `Synchronize_RealUserExample_AddsNewOperationOnly` — берём `example-existing.tf`, готовим OpenAPI с одной новой операцией, делаем sync, проверяем что: (а) валидный HCL на выходе, (б) parse'ится обратно, (в) есть ровно одна новая запись в `api_operations`, (г) все исходные записи присутствуют байт-в-байт.

### Phase 8 — Интеграция с ConversionOrchestrator

**Файлы**:

- `Application/Services/ConversionOrchestratorService.cs` — добавить метод `Sync()`
- `Domain/Models/SyncRequest.cs` — DTO с openApiJson, existingTerraform, settings, MergePolicy, OperationMatchStrategy
- `Application/DependencyInjection.cs` — все регистрации

**Изменения в orchestrator**:

```csharp
public sealed class ConversionOrchestratorService : IConversionOrchestrator
{
    // существующие зависимости
    private readonly IAppendOnlySynchronizer _synchronizer;
    private readonly IApimTerraformReader _reader;
    private readonly IOperationExecutionGraphBuilder _graphBuilder;

    // существующий Convert() — без изменений (или мы хотим тоже включить
    // ExecutionGraph как в исходном плане; это можно сделать отдельным PR)

    public SyncResult Sync(SyncRequest request)
    {
        var newConfig = _parser.Parse(request.OpenApiJson, request.Settings);
        var parsed = string.IsNullOrEmpty(request.ExistingTerraform)
            ? new ParsedApimDocument { Ast = new HclDocument() }  // пустой
            : _reader.Read(request.ExistingTerraform);

        var syncResult = _synchronizer.Synchronize(
            parsed,
            newConfig,
            request.MergePolicy ?? new MergePolicy(),
            request.MatchStrategy ?? new OperationMatchStrategy());

        // Опционально: построить ExecutionGraph поверх SyncReport
        var graph = _graphBuilder.BuildFromSyncReport(syncResult.Report, newConfig.ApiGroupName);
        return syncResult with { ExecutionGraph = graph };
    }
}
```

**Старый `Convert(json, settings, existingTerraform)`** делегирует в `Sync()` для backward-compat.

**Тесты** в `tests/TerraformApi.Application.Tests/Orchestrator/`:

- `Sync_FromScratch_GeneratesValidConfig`
- `Sync_WithExisting_PreservesExisting`
- `Sync_ProducesPopulatedSyncReport`

### Phase 9 — API endpoint + MCP tool

**Файлы**:

- `src/TerraformApi.Api/Endpoints/SyncEndpoint.cs` — `POST /api/sync`
- `src/TerraformApi.Mcp/Tools/SyncTool.cs` — MCP tool `sync_openapi_with_terraform`
- Обновить `update_terraform_from_openapi` — внутри делегировать в новый Sync с дефолтной append-only policy для backward-compat

**API контракт `POST /api/sync`**:

```json
{
  "openApiJson": "...",
  "existingTerraform": "...",
  "environment": "dev",
  "apiGroupName": "my-api-group",
  "settings": { ... остальные настройки ... },
  "mergePolicy": {
    "unknownOperationPolicy": "Preserve",
    "newOperationPolicy": "Append",
    "operationFieldOverrides": { "description": "Overwrite" }
  },
  "matchStrategy": {
    "keys": ["MethodAndUrl", "OperationId"],
    "urlNormalization": { "trimTrailingSlash": true, "treatLeadingSlashAsOptional": true }
  },
  "variableContext": { "env": "dev", "api_name": "bpc" }
}
```

**Response**: `SyncResult` сериализованный как JSON, с полем `terraformConfig` (string) и `report` (объект).

**Тесты**: добавить интеграционные тесты в `tests/TerraformApi.Api.Tests/`.

### Phase 10 — UI (опционально, отдельный PR)

- В существующем web UI добавить вкладку "Sync"
- Поля: OpenAPI input, existing Terraform input, advanced policy editor
- Output: дифф-вид (новые/без изменений/preserved/enriched), скачивание HCL

---

## 7. Что НЕ делаем в этой фиче (вынесено в отдельные задачи)

| Не делаем сейчас                                             | Когда                          | Причина                                                                             |
| ------------------------------------------------------------ | ------------------------------ | ----------------------------------------------------------------------------------- |
| Полный HCL2 парсер с поддержкой for-expressions, тернарников | Когда понадобится              | Текущее подмножество достаточно для APIM-конфига                                    |
| Парсинг файлов `.tfvars`                                     | Отдельная задача               | Не нужно для самой синхронизации; контекст переменных можно передать как Dictionary |
| Слияние с remote state (terraform.tfstate)                   | Отдельная задача               | Это совсем другая модель                                                            |
| Автоматический CI с `terraform validate`                     | Отдельная задача               | Зависит от наличия Terraform CLI; добавим в инфраструктуру                          |
| Поддержка YAML OpenAPI (только JSON сейчас)                  | Можно добавить позже           | Не блокирует основной флоу                                                          |
| Mermaid/CSV экспорт SyncReport                               | Phase 11 (после основной фичи) | Уже описано в существующем плане Operation Execution Graph                          |

---

## 8. Критерии готовности (Definition of Done)

LLM считает фичу законченной, когда **все** пункты выполнены:

- [ ] Все файлы из §1.1 созданы и компилируются
- [ ] Все тесты из §5 написаны и зелёные
- [ ] **Round-trip-тест** на `example-existing.tf` зелёный
- [ ] **Append-only-тест** на `example-existing.tf` + OpenAPI с 1 новой операцией: на выходе исходник + 1 запись, ничего не удалено
- [ ] Все 274 существующих теста остаются зелёными (никакой регрессии в Convert/Validate/Transform/Fetch)
- [ ] `POST /api/sync` отвечает 200 на валидный запрос
- [ ] MCP tool `sync_openapi_with_terraform` доступен и работает
- [ ] `ConversionOrchestratorService.Convert(json, settings, existingTerraform)` (старая сигнатура) делегирует в новый Sync и не ломает старые тесты
- [ ] Логирование: каждое примененное и пропущенное изменение логируется на уровне Information
- [ ] В `README.md` добавлен раздел "Append-only sync" с примером запроса/ответа
- [ ] В `docs/sync-policies.md` (новый файл) — таблица всех дефолтных политик с обоснованиями

---

## 9. Контрольный пример для финальной приёмки

**Дано**:

- `existingTerraform` = рабочий пример пользователя (целиком, без модификаций).
- `openApiJson` = OpenAPI 3.0 с:
  - Одной операцией, которая **уже есть** в существующем TF (тот же `operationId` после резолва переменных, тот же method+url)
  - Одной операцией, которая **отсутствует** в TF (новая)
  - Без операций, которые есть в TF, но отсутствуют в OpenAPI (то есть OpenAPI не претендует на полное покрытие)

**Запрос**: `POST /api/sync` с дефолтным `mergePolicy` (append-only) и `matchStrategy` = `[MethodAndUrl, OperationId]`.

**Ожидаемый результат**:

1. HCL на выходе **парсится** без ошибок.
2. HCL содержит **ровно одну новую запись** в `api_operations` (после исходных).
3. Все остальные строки идентичны исходнику (проверка: `originalHcl.Split('\n')[0..N]` == `resultHcl.Split('\n')[0..N]` для всех неизменённых блоков).
4. `SyncReport`:
   - `OperationsAdded = 1`
   - `OperationsIdentical = 1`
   - `OperationsPreserved = 0` (потому что OpenAPI не пытался удалять)
   - `Duplicates = []`
   - `Warnings` содержит как минимум `OperationIdContainsInterpolation` для существующих операций (потому что `operation_id = "${operation_prefix}-${env}"`)
5. `ExecutionGraph.Statistics.TotalOperations = 2`, `NewOperations = 1`, `IncludedOperations = 2`.

**Этот тест — обязательная финальная приёмка фичи.**

---

## 10. Подсказки и заметки для Opus

1. **Не пытайся написать сразу всё**. Иди по фазам. После каждой — запускай `dotnet build` и `dotnet test`. Если что-то красное — фиксь, прежде чем идти дальше.

2. **HCL Lexer и Parser — самый рискованный кусок**. Пиши тесты ДО реализации (TDD). Особенно P4, P5, P10, P11.

3. **AST round-trip — критерий правильности парсера**. Если round-trip ломает `example-existing.tf`, ничего другого делать нельзя — фикс сначала это.

4. **Когда не уверен, как обработать редкий случай** — добавь `HclParseDiagnostic` с уровнем Warning, не бросай исключение. Парсер должен быть толерантным.

5. **HclWriter должен иметь `PreserveOriginalFormatting = true` дефолтом**. Это значит: для каждого `HclNode` храним slice исходника по позициям. Если узел не модифицировался — выводим slice как есть. Только для новых/изменённых узлов — генерим текст из шаблона. Это и есть «гарантия минимальных изменений», которая нужна append-only.

6. **Никаких regex для извлечения операций**. Если ловишь себя на мысли «а тут проще regex'ом» — стоп: ты добавляешь техдолг, который вся эта фича призвана убрать. Reader работает только через AST.

7. **OperationFingerprint — record с value-equality**. Используй record'ы, не классы. Это упрощает сравнение и индексирование (HashSet, Dictionary).

8. **Все public records — `sealed`**. Это соглашение проекта (см. существующие `ApimApi`, `ApimApiOperation` и т.д.).

9. **DI: всё новое регистрируется как `Singleton`** (нет состояния между вызовами).

10. **Логирование**: каждое изменение AST логируется. Используй existing `ILogger<T>` pattern из проекта. Пример: `_logger.LogInformation("Enriched operation {OperationId}: field {FieldPath} set to {NewValue}", opId, field, value);`.

11. **Когда дописываешь `Sync()` в orchestrator, не удаляй существующий `Convert()`**. Сделай `Convert()` обёрткой, чтобы старые тесты (173 в Application + 34 в Api + 67 в Mcp) не упали.

12. **Документация в коде**: каждый public record/interface должен иметь `///` summary. Это не опция, это требование проекта.

---

---

# REVISION 1 — Дополнения (TemplateProfile, Style Detection, Comments, MCP)

Этот раздел **дополняет** основной план §1–§9. Если что-то здесь противоречит более раннему тексту — приоритет за REVISION 1.

## §REV-1. UX-инвариант, который мы хотим обеспечить

Пользователь должен иметь возможность сделать **одно** из следующих действий и получить корректный результат **без дополнительной настройки**:

1. **Вставить только OpenAPI** → получить Terraform с шаблонизированными значениями (`${apim_name}`, `${env}` и т. д.), плюс шапка-комментарий со списком плейсхолдеров для замены.
2. **Вставить OpenAPI + существующий Terraform-файл (любого вида)** → получить тот же файл с добавленными новыми методами, оформленными **в том же стиле**, что и существующие (литерал → литерал, шаблон → шаблон), плюс комментарии-описания над каждой вставленной операцией.
3. **Вставить только Terraform-файл** → получить **анализ**: список API-групп `(apim_resource_group_name, api_name)`, список операций в каждой группе, обнаруженные плейсхолдеры, рекомендуемый `ApimTemplateProfile`.

Каждый из этих сценариев работает **без указания режима** — система определяет его сама по входу.

---

## §REV-1.2. Реестр плейсхолдеров и `ApimTemplateProfile`

### REV-1.2.1. Полный реестр (на основе рабочего примера пользователя + рекомендуемые расширения)

| Слой          | Поле HCL                           | Плейсхолдер по умолчанию                                                                            | Категория      | Обязательность                         |
| ------------- | ---------------------------------- | --------------------------------------------------------------------------------------------------- | -------------- | -------------------------------------- |
| api           | `apim_resource_group_name`         | `${stage_group_name}`                                                                               | Infrastructure | **must templatize**                    |
| api           | `apim_name`                        | `${apim_name}`                                                                                      | Infrastructure | **must templatize**                    |
| api           | `name`                             | `${api_name}-${env}`                                                                                | Identity       | **must templatize**                    |
| api           | `display_name`                     | `${api_display_name} - ${env}`                                                                      | Identity       | **must templatize**                    |
| api           | `path`                             | `${api_path_prefix}.${env}/v1/${api_path_suffix}`                                                   | Routing        | **must templatize**                    |
| api           | `service_url`                      | `https://${api_gateway_host}/${api_version}/${backend_service_path}/`                               | Routing        | **must templatize**                    |
| api           | `revision`                         | `${api_revision}`                                                                                   | Versioning     | nice-to-have                           |
| api           | `product_id`                       | `${product_id}`                                                                                     | Authorization  | only when set                          |
| api           | `subscription_key_parameter_names` | `${subscription_key_parameter_names}`                                                               | Authorization  | only when set                          |
| api           | `protocols`                        | `["https"]` (литерал)                                                                               | Security       | НЕ шаблонизируем                       |
| api           | `soap_pass_through`                | `false` (литерал)                                                                                   | Capabilities   | НЕ шаблонизируем                       |
| api           | `subscription_required`            | `${subscription_required}`                                                                          | Authorization  | recommended                            |
| api_operation | `operation_id`                     | `${operation_prefix}-${env}`                                                                        | Identity       | **must templatize**                    |
| api_operation | `apim_resource_group_name`         | `${stage_group_name}`                                                                               | Infrastructure | **must templatize**                    |
| api_operation | `apim_name`                        | `${apim_name}`                                                                                      | Infrastructure | **must templatize**                    |
| api_operation | `api_name`                         | `${api_name}-${env}`                                                                                | Identity       | **must templatize**                    |
| api_operation | `display_name`                     | значение из OpenAPI `summary` (литерал)                                                             | Documentation  | НЕ шаблонизируем                       |
| api_operation | `method`                           | `GET` / `POST` / ... (литерал)                                                                      | Routing        | **НЕЛЬЗЯ** — APIM enum                 |
| api_operation | `url_template`                     | значение из OpenAPI `path` (литерал)                                                                | Routing        | НЕ шаблонизируем (это и есть контракт) |
| api_operation | `status_code`                      | `"200"` (литерал)                                                                                   | Routing        | НЕ шаблонизируем                       |
| api_operation | `description`                      | из OpenAPI (литерал)                                                                                | Documentation  | НЕ шаблонизируем                       |
| CORS policy   | `<origin>` URLs                    | `https://${frontend_host}.${env}.${company_domain}` + `https://${local_dev_host}:${local_dev_port}` | CORS           | **must templatize**                    |

### REV-1.2.2. Дополнительные плейсхолдеры (моя рекомендация — ЧТО ЕЩЁ ИМЕЕТ СМЫСЛ ВЫНЕСТИ)

В вашем текущем примере этих переменных нет, но я предлагаю добавить (опционально, через расширенный профиль):

| Плейсхолдер                           | Где применяется                                     | Зачем                                                                                                    |
| ------------------------------------- | --------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `${env}`                              | везде, как суффикс/инфикс                           | Уже у вас есть, но стоит зафиксировать как «канонический» — это самая частая переменная                  |
| `${api_version}`                      | в `service_url` и опционально в `path`              | У вас зашит в `service_url`, но имеет смысл иметь и в `name`/`path` для версионных API (`my-api-v2-dev`) |
| `${tenant_id}` / `${subscription_id}` | в backend ARM-resource-id ссылках, в OAuth policy   | Когда APIM ссылается на ресурсы по ARM-ID (key vault, identity, log analytics)                           |
| `${backend_url_protocol}`             | в `service_url`                                     | Для разработки можно `http`, для prod `https`; снимает hardcode `https://`                               |
| `${cors_allow_credentials}`           | в CORS policy                                       | `true` для prod с auth, `false` для public API                                                           |
| `${subscription_key_header_name}`     | в `subscription_key_parameter_names`                | По умолчанию `Ocp-Apim-Subscription-Key`, но иногда custom                                               |
| `${rate_limit_calls_per_minute}`      | в inbound rate-limit policy                         | Разный лимит по средам (dev permissive, prod strict)                                                     |
| `${oauth_authority_url}`              | в `validate-jwt` policy                             | Когда включена OAuth-проверка; разный AAD-tenant по среде                                                |
| `${oauth_audience}`                   | там же                                              | Audience claim                                                                                           |
| `${log_analytics_workspace_id}`       | в `log-to-eventhub` или Application Insights policy | Подключение логирования                                                                                  |
| `${product_subscription_required}`    | в product config                                    | Если генерируется product                                                                                |
| `${product_approval_required}`        | в product config                                    | То же                                                                                                    |
| `${api_revision_description}`         | в `revision_description` поле                       | Не у всех модулей есть, опционально                                                                      |

**Все «дополнительные» плейсхолдеры по умолчанию отключены** — они активируются только если соответствующая фича используется (например, `${oauth_authority_url}` появляется только если в политике есть `<validate-jwt>`).

### REV-1.2.3. Доменная модель `ApimTemplateProfile`

Файл: `src/TerraformApi.Domain/Models/Sync/ApimTemplateProfile.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

/// Профиль шаблонизации: какие HCL-поля → какие Terraform-выражения.
public sealed record ApimTemplateProfile
{
    public required string Name { get; init; }

    /// Маппинг "имя поля api" → "значение для HCL" (как будет выведено внутри кавычек,
    /// с ${...} интерполяциями).
    /// Если поле отсутствует в словаре → значение берётся из OpenAPI/настроек литералом.
    public IReadOnlyDictionary<string, string> ApiFieldTemplates { get; init; }
        = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> OperationFieldTemplates { get; init; }
        = new Dictionary<string, string>();

    /// Шаблон для operation_id, поддерживающий подстановку.
    /// Если содержит `{op}` — это будет заменено на operationId из OpenAPI (нормализованный).
    /// По умолчанию — общий префикс без подстановки: `${operation_prefix}-${env}`.
    /// Альтернатива: `${operation_prefix}-{op}-${env}` → каждая операция получит уникальный шаблон.
    public string? OperationIdTemplate { get; init; }

    public CorsTemplateVariables CorsVariables { get; init; } = new();

    /// Использовать ли литералы для url_template и method (рекомендуется true —
    /// потому что это и есть контракт API).
    public bool KeepRoutingFieldsLiteral { get; init; } = true;

    /// Шаблонизировать ли display_name (по умолчанию — оставляем literal из OpenAPI summary).
    public bool TemplatizeDisplayName { get; init; } = false;

    /// =============== Готовые профили ===============

    /// Профиль 1 — точно соответствует рабочему примеру пользователя.
    public static readonly ApimTemplateProfile UserExampleProfile = new()
    {
        Name = "UserExampleProfile",
        ApiFieldTemplates = new Dictionary<string, string>
        {
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"]                = "${apim_name}",
            ["name"]                     = "${api_name}-${env}",
            ["display_name"]             = "${api_display_name} - ${env}",
            ["path"]                     = "${api_path_prefix}.${env}/v1/${api_path_suffix}",
            ["service_url"]              = "https://${api_gateway_host}/${api_version}/${backend_service_path}/",
            ["revision"]                 = "${api_revision}",
            ["product_id"]               = "${product_id}"
        },
        OperationFieldTemplates = new Dictionary<string, string>
        {
            ["operation_id"]             = "${operation_prefix}-${env}",
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"]                = "${apim_name}",
            ["api_name"]                 = "${api_name}-${env}"
        },
        OperationIdTemplate = "${operation_prefix}-${env}"
    };

    /// Профиль 2 — расширенный, со всеми «рекомендуемыми» плейсхолдерами.
    public static readonly ApimTemplateProfile ExtendedProfile = new()
    {
        Name = "ExtendedProfile",
        ApiFieldTemplates = new Dictionary<string, string>
        {
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"]                = "${apim_name}",
            ["name"]                     = "${api_name}-${api_version}-${env}",
            ["display_name"]             = "${api_display_name} - ${env}",
            ["path"]                     = "${api_path_prefix}.${env}/${api_version}/${api_path_suffix}",
            ["service_url"]              = "${backend_url_protocol}://${api_gateway_host}/${api_version}/${backend_service_path}/",
            ["revision"]                 = "${api_revision}",
            ["product_id"]               = "${product_id}",
            ["subscription_required"]    = "${subscription_required}"
        },
        OperationFieldTemplates = new Dictionary<string, string>
        {
            ["operation_id"]             = "${operation_prefix}-{op}-${env}",
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"]                = "${apim_name}",
            ["api_name"]                 = "${api_name}-${api_version}-${env}"
        },
        OperationIdTemplate = "${operation_prefix}-{op}-${env}"
    };

    /// Профиль 3 — без шаблонизации, всё литералом (для разовой генерации).
    public static readonly ApimTemplateProfile LiteralProfile = new()
    {
        Name = "LiteralProfile",
        ApiFieldTemplates = new Dictionary<string, string>(),
        OperationFieldTemplates = new Dictionary<string, string>(),
        OperationIdTemplate = null,
        KeepRoutingFieldsLiteral = true
    };
}

public sealed record CorsTemplateVariables
{
    public string FrontendHostExpr { get; init; } = "${frontend_host}";
    public string EnvExpr { get; init; } = "${env}";
    public string CompanyDomainExpr { get; init; } = "${company_domain}";
    public string LocalDevHostExpr { get; init; } = "${local_dev_host}";
    public string LocalDevPortExpr { get; init; } = "${local_dev_port}";
    public string AllowCredentialsExpr { get; init; } = "true"; // или "${cors_allow_credentials}"
}
```

### REV-1.2.4. Алгоритм применения профиля при генерации

Когда `TerraformGeneratorService.Generate(config, profile)` строит AST для нового HCL:

```
для каждого поля field в api / api_operation:
    template = profile.<Section>FieldTemplates.GetValueOrDefault(field)
    if template != null:
        # шаблон — кладём интерполяцию
        node = HclInterpolation { InnerText = applyOpIdSubstitution(template, op?.OperationId) }
    else:
        # литерал из OpenAPI или дефолта
        node = HclLiteral { Kind=String, RawValue = literalValue }
    write node into AST under field
```

Функция `applyOpIdSubstitution(template, opId)`:

- Если `template` содержит `{op}` → заменяем на нормализованный `opId` (snake_case или kebab-case по конфигу).
- Иначе возвращаем `template` без изменений.

**Пример нормализации**: OpenAPI operationId `listUserById` → kebab `list-user-by-id` → шаблон `${operation_prefix}-list-user-by-id-${env}`.

### REV-1.2.5. `IApimTemplateProfileApplier`

```csharp
public interface IApimTemplateProfileApplier
{
    /// Применяет профиль к существующему AST: заменяет литералы соответствующих
    /// полей на интерполяции (если профиль их шаблонизирует) И/ИЛИ
    /// добавляет недостающие поля.
    ///
    /// ВАЖНО: эта операция МОЖЕТ изменить значения литералов, поэтому она
    /// НЕ append-only. Используется только в режиме явной конвертации
    /// "literal → templated" через MCP-тул apply_template_profile.
    ParsedApimDocument Apply(
        ParsedApimDocument document,
        ApimTemplateProfile profile,
        ApplyProfileOptions options);

    /// Обратная операция: подставляет значения переменных в плейсхолдеры,
    /// получая "резолвленный" литералный HCL для конкретной среды.
    ParsedApimDocument Resolve(
        ParsedApimDocument document,
        IReadOnlyDictionary<string, string> variableValues);
}

public sealed record ApplyProfileOptions
{
    /// Применять профиль к полям, у которых уже есть значение?
    /// false (default) — только к пустым/отсутствующим.
    public bool OverwriteExisting { get; init; }

    /// Добавлять REPLACE BEFORE APPLY комментарии перед изменёнными блоками.
    public bool AddReplaceComments { get; init; } = true;
}
```

---

## §REV-1.3. Детекция стиля + auto-grouping

### REV-1.3.1. `IApimTemplateProfileDetector`

Когда пользователь вставил существующий файл, мы должны понять: какой стиль он использует, какие у него уже есть плейсхолдеры. На основе этого мы строим **продолжение** стиля при вставке новых операций.

Файл: `src/TerraformApi.Domain/Interfaces/IApimTemplateProfileDetector.cs`.

```csharp
public interface IApimTemplateProfileDetector
{
    /// Анализирует существующий HCL и возвращает обнаруженный профиль
    /// + диагностику + предложения.
    DetectedProfile Detect(ParsedApimDocument document);
}

public sealed record DetectedProfile
{
    /// Профиль, **построенный на основе фактических данных файла**.
    /// Его можно сразу использовать для генерации новых операций в том же стиле.
    public required ApimTemplateProfile InferredProfile { get; init; }

    /// Какие поля встречались как интерполяции (с указанием частоты).
    public List<DetectedField> DetectedFields { get; init; } = [];

    /// Все ${...} имена, которые встретились хотя бы раз. Это "глобальный словарь"
    /// переменных, которые пользователь должен будет определить в .tfvars.
    public HashSet<string> AllReferencedVariables { get; init; } = [];

    /// Найденные значения литералов (например, "apim-company-dev" встречается
    /// в `apim_name` всех операций). Полезно для предложения автозаполнения
    /// при генерации новых.
    public IReadOnlyDictionary<string, List<string>> LiteralValuesByField { get; init; }
        = new Dictionary<string, List<string>>();

    public StylingConfidence Confidence { get; init; }

    /// Какой готовый профиль (UserExample/Extended/Literal) ближе всего к detected.
    public string? ClosestKnownProfileName { get; init; }
}

public sealed record DetectedField
{
    public required string FieldPath { get; init; }       // "api.apim_name" или "api_operation.operation_id"
    public int TemplatedOccurrences { get; init; }
    public int LiteralOccurrences { get; init; }
    public List<string> ObservedExpressions { get; init; } = [];   // напр. ["${apim_name}"]
    public List<string> ObservedLiterals { get; init; } = [];      // напр. ["apim-company-dev"]
}

public enum StylingConfidence
{
    /// >70% полей в файле — интерполяции.
    HighlyTemplated,
    /// 30-70% — смешанный.
    Mixed,
    /// <30% — почти всё литералом.
    MostlyLiteral,
    /// Файл пустой / нет операций.
    Empty
}
```

### REV-1.3.2. Алгоритм детекции

```
function Detect(document):
    fields = {}  # field_path -> DetectedField
    allVars = set()

    for group in document.ApiGroups:
        for api in group.Apis:
            for fieldName in ["apim_resource_group_name","apim_name","name",
                              "display_name","path","service_url","revision",
                              "product_id","subscription_required"]:
                value = api.AstNode.Get(fieldName)
                track(fields, "api."+fieldName, value, allVars)

        for op in group.Operations:
            for fieldName in ["operation_id","apim_resource_group_name","apim_name",
                              "api_name","display_name","method","url_template",
                              "status_code","description"]:
                value = op.AstNode.Get(fieldName)
                track(fields, "api_operation."+fieldName, value, allVars)

    total_templated = sum(f.TemplatedOccurrences for f in fields.values())
    total_literal = sum(f.LiteralOccurrences for f in fields.values())
    total = total_templated + total_literal

    if total == 0: confidence = Empty
    elif total_templated/total > 0.7: confidence = HighlyTemplated
    elif total_templated/total > 0.3: confidence = Mixed
    else: confidence = MostlyLiteral

    inferredProfile = buildProfileFromMostCommonExpressions(fields)
    closestKnown = matchToKnownProfile(inferredProfile)

    return DetectedProfile(inferredProfile, fields, allVars, ..., confidence, closestKnown)
```

`buildProfileFromMostCommonExpressions` — для каждого field, если в `ObservedExpressions` есть expression, встречающийся в большинстве случаев (>50% от непустых), он становится template'ом этого field в выводимом профиле. Если поле в файле всегда литерал — оно НЕ попадает в `ApiFieldTemplates` (т. е. новые операции тоже будут литералом).

### REV-1.3.3. Auto-grouping по `(apim_resource_group_name, api_name)`

Модель:

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record ApimApiGroupKey
{
    /// Структурное представление — как было в HCL (с ${...} или литерал).
    public required string ApimResourceGroupNameRaw { get; init; }
    public required string ApiNameRaw { get; init; }

    /// Резолвленные значения (если был передан variable context).
    public string? ApimResourceGroupNameResolved { get; init; }
    public string? ApiNameResolved { get; init; }

    /// Использовать резолвленные значения для equality, если они есть, иначе raw.
    public bool Equals(ApimApiGroupKey? other)
    {
        if (other is null) return false;
        var thisRg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
        var thisApi = ApiNameResolved ?? ApiNameRaw;
        var otherRg = other.ApimResourceGroupNameResolved ?? other.ApimResourceGroupNameRaw;
        var otherApi = other.ApiNameResolved ?? other.ApiNameRaw;
        return string.Equals(thisRg, otherRg, StringComparison.OrdinalIgnoreCase)
            && string.Equals(thisApi, otherApi, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        var rg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
        var api = ApiNameResolved ?? ApiNameRaw;
        return HashCode.Combine(rg.ToLowerInvariant(), api.ToLowerInvariant());
    }
}
```

### REV-1.3.4. Обновление `ParsedApimDocument`

К полям из §2.2 добавляется:

```csharp
public sealed record ParsedApimDocument
{
    // ... существующие поля ...

    /// Группировка api-блоков и их операций по (rg, name).
    /// КЛЮЧЕВО для sync: новые операции из OpenAPI должны попасть в правильную группу.
    public IReadOnlyDictionary<ApimApiGroupKey, ApiGroupBundle> ApisByGroupKey { get; init; }
        = new Dictionary<ApimApiGroupKey, ApiGroupBundle>();
}

public sealed record ApiGroupBundle
{
    public required ApimApiGroupKey Key { get; init; }
    public required ParsedApi Api { get; init; }
    /// Операции, у которых `apim_resource_group_name + api_name` совпадает с Key.
    public List<ParsedApiOperation> Operations { get; init; } = [];
}
```

**Алгоритм построения** (в `ApimTerraformReaderService` после извлечения):

```
for group in ApiGroups:
    for op in group.Operations:
        rg = op.AstNode.Get("apim_resource_group_name")?.StructuralText
        apiName = op.AstNode.Get("api_name")?.StructuralText
        key = ApimApiGroupKey(rg, apiName)
        bundle = ApisByGroupKey.GetOrAdd(key)
        bundle.Operations.Add(op)

    for api in group.Apis:
        rg = api.AstNode.Get("apim_resource_group_name")?.StructuralText
        apiName = api.AstNode.Get("name")?.StructuralText
        key = ApimApiGroupKey(rg, apiName)
        bundle = ApisByGroupKey.GetOrAdd(key)
        bundle.Api = api
```

### REV-1.3.5. Выбор целевой группы при sync

В `AppendOnlySynchronizer.Synchronize` теперь:

```
function Synchronize(parsed, newConfig, policy, strategy):
    detector = ApimTemplateProfileDetector()
    detected = detector.Detect(parsed)

    # Выбираем профиль для новых операций
    effectiveProfile = options.OverrideProfile ?? detected.InferredProfile

    # Определяем целевую группу
    targetKey = ApimApiGroupKey
    {
        ApimResourceGroupNameRaw = newConfig.Settings.StageGroupName,
        ApiNameRaw = newConfig.Settings.ApiName
    }

    bundle = parsed.ApisByGroupKey.TryGetValue(targetKey, out var existing)
        ? existing
        : createNewBundle(targetKey, ...)

    # дальше — как раньше, но с использованием effectiveProfile при построении новых HclObject
```

Это даёт UX-инвариант: пользователь не указывает «куда вставлять» — система сама находит правильную `(rg, api_name)` группу по настройкам, переданным в запросе.

---

## §REV-1.4. Обновление взаимодействия `OperationMatcher` с группами

Когда в файле несколько API-групп (например, `bpc-api-dev` и `bpc-internal-dev`), матчинг операций должен **сначала отфильтровать только нужную группу**, потом уже искать соответствия. Иначе одна и та же `GET /users` в разных API будет матчиться как «дубликат», хотя это разные API.

Изменение в `OperationMatcher.Match`:

```csharp
MatchResult Match(
    IReadOnlyList<OperationFingerprint> openApiFingerprints,
    IReadOnlyList<OperationFingerprint> terraformFingerprints,
    OperationMatchStrategy strategy,
    ApimApiGroupKey? scopeKey = null);  // НОВОЕ
```

Если `scopeKey != null`, то перед матчингом из `terraformFingerprints` оставляем только те, у которых `Fingerprint.ApiName` и `Fingerprint.ApiResourceGroup` (новое поле в Fingerprint) совпадают со `scopeKey`.

Добавить в `OperationFingerprint`:

```csharp
public string? ApiResourceGroup { get; init; }
```

И в матчинг-ключи:

```csharp
public enum OperationMatchKey
{
    OperationId,
    MethodAndUrl,
    MethodAndUrlAndParams,
    Tag,
    ApiAndMethodAndUrl,
    RgApiAndMethodAndUrl,     // НОВОЕ — самый строгий scope
    Custom
}
```

---

## §REV-1.5. Комментарии в AST и формат заголовков операций

### REV-1.5.1. AST-узлы для комментариев

```csharp
namespace TerraformApi.Domain.Models.Hcl;

/// Элемент тела объекта или массива.
/// Может быть либо присваиванием, либо комментарием (которые мы хотим сохранить).
public abstract record HclObjectItem : HclNode;

public sealed record HclAssignment : HclObjectItem
{
    public required string Key { get; init; }
    public required HclValue Value { get; init; }
    public bool KeyIsQuoted { get; init; }
}

public sealed record HclComment : HclObjectItem
{
    public required string Text { get; init; }        // без префикса '#' / '//' / '/* */'
    public required HclCommentKind Kind { get; init; }
    /// True если это блок комментариев (несколько подряд) — для группировки на запись.
    public bool IsLeading { get; init; }
}

public enum HclCommentKind { LineHash, LineSlash, Block }
```

**Изменения в `HclObject` и `HclArray`**:

```csharp
public sealed record HclObject : HclValue
{
    // Было: public List<HclAssignment> Assignments { get; init; }
    // Стало:
    public List<HclObjectItem> Items { get; init; } = [];

    /// Удобный фильтр, возвращающий только assignments.
    public IEnumerable<HclAssignment> Assignments => Items.OfType<HclAssignment>();

    public HclValue? Get(string key) =>
        Assignments.FirstOrDefault(a => a.Key == key)?.Value;
}

public sealed record HclArray : HclValue
{
    // Элементы массива могут предваряться комментариями.
    public List<HclArrayItem> Items { get; init; } = [];
}

public sealed record HclArrayItem : HclNode
{
    /// Комментарии непосредственно перед этим элементом.
    public List<HclComment> LeadingComments { get; init; } = [];
    public required HclValue Value { get; init; }
}
```

### REV-1.5.2. Лексер и парсер — поддержка комментариев

В §4.1 лексер уже пропускает комментарии. **Теперь нужно их сохранять**:

- Лексер эмитит `COMMENT` токены (раньше пропускал).
- Парсер при чтении тела `HclObject` или `HclArray`:
  - Накапливает «висящие» комментарии перед следующим assignment/item
  - При встрече assignment'а — оборачивает накопленные комментарии в `HclArrayItem.LeadingComments` или вставляет как отдельные `HclComment` в `HclObject.Items` непосредственно перед `HclAssignment`.

### REV-1.5.3. Writer — вывод комментариев

`HclWriterService` при выводе:

- Каждый `HclComment` в `HclObject.Items` выводится отдельной строкой с правильным отступом.
- Каждый `HclArrayItem.LeadingComments` выводится перед самим элементом, с тем же отступом, что и элемент.

Формат вывода `HclComment`:

```
LineHash:   "# " + Text
LineSlash:  "// " + Text
Block:      "/* " + Text + " */"
```

Если в исходнике был `# foo` — на выходе тоже `# foo`. Стиль не меняем.

### REV-1.5.4. Формат комментариев перед операцией (требование пользователя)

Каждая вставленная операция получает **блок из нескольких комментариев** в качестве `LeadingComments`:

```hcl
api_operations = [
    # GET /users/{id}  |  op_id: getUserById
    # display_name: "Get user by id" · source: OpenAPI · inserted: 2026-06-12 by sync
    # placeholders to replace: ${stage_group_name}, ${apim_name}, ${api_name}, ${env}, ${operation_prefix}
    {
        operation_id             = "${operation_prefix}-${env}"
        apim_resource_group_name = "${stage_group_name}"
        apim_name                = "${apim_name}"
        api_name                 = "${api_name}-${env}"
        display_name             = "Get user by id"
        method                   = "GET"
        url_template             = "/users/{id}"
        status_code              = "200"
        description              = ""
    },
]
```

**Строго**: первая строка комментария содержит **только** `METHOD URL_TEMPLATE | op_id: <id>`. Остальные детали — со второй строки. Это требование пользователя.

Если у операции нет плейсхолдеров (LiteralProfile) — третья строка с `placeholders to replace:` пропускается.

### REV-1.5.5. `IOperationCommentBuilder` и `OperationCommentSpec`

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record OperationCommentSpec
{
    public required string Method { get; init; }
    public required string UrlTemplate { get; init; }
    public required string OperationId { get; init; }
    public string? DisplayName { get; init; }
    public required OperationCommentSource Source { get; init; }
    public DateTime InsertedAt { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<string> PlaceholdersToReplace { get; init; } = [];
}

public enum OperationCommentSource
{
    OpenApi,
    Generated,
    ManuallyAdded,
    PreservedFromExisting
}

public interface IOperationCommentBuilder
{
    /// Строит список комментариев согласно правилам §REV-1.5.4.
    List<HclComment> Build(OperationCommentSpec spec);

    /// Сканирует HclObject (новую вставленную операцию) и извлекает все
    /// уникальные ${name} плейсхолдеры из её полей.
    IReadOnlyList<string> ExtractPlaceholders(HclObject operationNode);
}
```

Реализация `OperationCommentBuilderService`:

```csharp
public List<HclComment> Build(OperationCommentSpec spec)
{
    var comments = new List<HclComment>();

    // Строка 1: "GET /users/{id}  |  op_id: getUserById"
    comments.Add(new HclComment
    {
        Kind = HclCommentKind.LineHash,
        IsLeading = true,
        Text = $" {spec.Method.ToUpperInvariant()} {spec.UrlTemplate}  |  op_id: {spec.OperationId}"
    });

    // Строка 2: остальная информация
    var displayPart = string.IsNullOrEmpty(spec.DisplayName)
        ? ""
        : $"display_name: \"{spec.DisplayName}\" · ";
    var sourcePart = $"source: {spec.Source} · ";
    var datePart = $"inserted: {spec.InsertedAt:yyyy-MM-dd}";
    comments.Add(new HclComment
    {
        Kind = HclCommentKind.LineHash,
        IsLeading = true,
        Text = $" {displayPart}{sourcePart}{datePart}"
    });

    // Строка 3: только если есть плейсхолдеры
    if (spec.PlaceholdersToReplace.Count > 0)
    {
        comments.Add(new HclComment
        {
            Kind = HclCommentKind.LineHash,
            IsLeading = true,
            Text = $" placeholders to replace: {string.Join(", ", spec.PlaceholdersToReplace)}"
        });
    }

    return comments;
}
```

### REV-1.5.6. Шапка-блок перед массивом `api_operations`

Когда хоть одна операция содержит плейсхолдеры, перед самим массивом `api_operations` (или его новой частью при append) добавляется блок-комментарий со списком всех уникальных плейсхолдеров файла:

```hcl
# ============================================================================
# REPLACE BEFORE APPLY: define these variables in .tfvars or via -var:
#   ${stage_group_name}  ${apim_name}  ${api_name}  ${env}  ${operation_prefix}
#   ${frontend_host}  ${company_domain}  ${local_dev_host}  ${local_dev_port}
# ============================================================================
api_operations = [
    ...
]
```

**Если файл существующий и шапка уже есть** — мы её **не дублируем**. Детектится по подстроке `REPLACE BEFORE APPLY` в первом комментарии перед массивом. Если шапки нет, но добавляется хотя бы одна новая операция с плейсхолдерами — шапка добавляется.

### REV-1.5.7. Извлечение плейсхолдеров

`ExtractPlaceholders(HclObject)` рекурсивно обходит все вложенные узлы и собирает `ReferencedExpressions` из всех `HclInterpolation`. Дедуплицирует. Сортирует. Возвращает.

---

## §REV-1.6. Изменения в `IApimTerraformWriter` и `BuildFromConfiguration`

Сигнатура `BuildFromConfiguration` (§3.4) **меняется**:

```csharp
public interface IApimTerraformWriter
{
    string Write(ParsedApimDocument parsed, HclWriteOptions? options = null);

    /// Строит ParsedApimDocument с применением шаблонного профиля.
    /// Каждая операция получает leading-комментарии.
    ParsedApimDocument BuildFromConfiguration(
        ApimConfiguration configuration,
        BuildOptions options);
}

public sealed record BuildOptions
{
    public ApimTemplateProfile Profile { get; init; } = ApimTemplateProfile.UserExampleProfile;
    public IReadOnlyList<string>? ApiGroupParentPath { get; init; }
        = new[] { "apis", "bpc_apis", "backend_apis" };
    public bool AddOperationComments { get; init; } = true;
    public bool AddReplaceBeforeApplyHeader { get; init; } = true;
    public OperationCommentSource CommentSource { get; init; } = OperationCommentSource.OpenApi;
}
```

Алгоритм `BuildFromConfiguration`:

```
1. Создать пустой HclDocument
2. Создать цепочку HclObject'ов по ApiGroupParentPath
3. На дне создать HclAssignment ключом ApiGroupName (KeyIsQuoted, если содержит ${...})
4. Внутри добавить three assignments: product = [], api = [...], api_operations = [...]
5. Для каждого api в configuration:
   a. Построить HclObject применением profile.ApiFieldTemplates
   b. Добавить в api[]
6. Для каждой operation:
   a. Сгенерировать operation_id через profile.OperationIdTemplate (с substitution)
   b. Построить HclObject применением profile.OperationFieldTemplates
   c. Сгенерировать LeadingComments через OperationCommentBuilder
   d. Завернуть в HclArrayItem { LeadingComments, Value }
   e. Добавить в api_operations[]
7. Если AddReplaceBeforeApplyHeader: собрать все уникальные плейсхолдеры,
   добавить блок-комментарий перед api_operations
8. Вернуть ParsedApimDocument
```

---

## §REV-1.7. Изменения в фазах реализации (§6)

Существующие фазы остаются, но **дополняются**:

### Phase 1 (HCL Lexer/Parser/Writer) — РАСШИРЕНО

Дополнительные подзадачи:

- `HclComment.cs`, `HclObjectItem.cs`, `HclArrayItem.cs` (новые AST-типы)
- Lexer эмитит COMMENT-токены (не пропускает)
- Parser накапливает leading comments перед каждым item
- Writer выводит comments с правильным отступом

Дополнительные тесты:

- C1: Парсинг `# foo\n a = 1` → HclObject с двумя Items (Comment, Assignment)
- C2: Round-trip с комментариями (пишем тот же текст)
- C3: Комментарии перед элементами массива
- C4: Multi-line block comment `/* ... */`

### Phase 2 — ДОБАВЛЕНО

Новые файлы:

- `ApimTemplateProfile.cs`
- `CorsTemplateVariables.cs`
- `DetectedProfile.cs`, `DetectedField.cs`, `StylingConfidence.cs`
- `ApimApiGroupKey.cs`, `ApiGroupBundle.cs`
- `OperationCommentSpec.cs`, `OperationCommentSource.cs`
- `BuildOptions.cs`, `ApplyProfileOptions.cs`

Тесты: проверки дефолтов трёх готовых профилей.

### Phase 3 (Reader/Writer) — РАСШИРЕНО

Дополнительные подзадачи:

- В `ApimTerraformReaderService` — построение `ApisByGroupKey`
- В `ApimTerraformWriterService.BuildFromConfiguration` — применение `BuildOptions`
- В `BuildFromConfiguration` — генерация leading comments через DI'инжектированный `IOperationCommentBuilder`

Дополнительные тесты:

- T1: BuildFromConfiguration с UserExampleProfile → AST содержит ровно те плейсхолдеры, что в Profile.ApiFieldTemplates
- T2: BuildFromConfiguration с LiteralProfile → AST без интерполяций
- T3: BuildFromConfiguration → каждая операция имеет 2 или 3 leading-comments в правильном формате
- T4: Reader корректно строит ApisByGroupKey для рабочего примера пользователя (там одна группа)
- T5: Reader корректно строит ApisByGroupKey для файла с двумя разными `(rg, api_name)` парами

### Phase 4а (НОВАЯ) — `ApimTemplateProfileDetector`

Файл: `ApimTemplateProfileDetectorService.cs`.

Тесты:

- DT1: Detect на пустом документе → Confidence=Empty
- DT2: Detect на рабочем примере пользователя → Confidence=HighlyTemplated, InferredProfile.ApiFieldTemplates содержит правильные плейсхолдеры
- DT3: Detect на файле с литералами → Confidence=MostlyLiteral, InferredProfile.ApiFieldTemplates пуст
- DT4: Detect на смешанном файле → Confidence=Mixed, в InferredProfile попадают только поля с >50% templated occurrences
- DT5: AllReferencedVariables содержит уникальный набор всех `${...}` из файла

### Phase 4b (НОВАЯ) — `OperationCommentBuilder`

Тесты:

- CB1: Build для OpenAPI-операции без плейсхолдеров → 2 комментария
- CB2: Build с плейсхолдерами → 3 комментария, третий содержит правильный список
- CB3: ExtractPlaceholders на HclObject с 5 разными `${...}` → 5 уникальных имён, отсортированы
- CB4: Формат первой строки — точно `METHOD URL  |  op_id: ID` (без лишнего)

### Phase 7 (AppendOnlySynchronizer) — РАСШИРЕНО

Дополнительные подзадачи:

- В начале `Synchronize`: вызвать `ApimTemplateProfileDetector` для определения стиля
- Если `SyncOptions.OverrideProfile == null` — использовать `detected.InferredProfile`
- Поиск целевой группы через `ApisByGroupKey` (а не первой попавшейся)
- При вставке новой операции — генерация leading-comments через `OperationCommentBuilder`
- При обновлении шапки `api_operations` (добавление REPLACE BEFORE APPLY) — детект существующей шапки

Дополнительные тесты:

- S13: Файл существует с HighlyTemplated стилем + OpenAPI новая операция → новая операция в той же стилистике
- S14: Файл с MostlyLiteral + OpenAPI новая операция → новая операция тоже литералом
- S15: Файл с двумя api_group, sync только в одну → вторая группа байт-в-байт идентична
- S16: Каждая вставленная операция имеет 2 или 3 leading-comments
- S17: При наличии плейсхолдеров — добавлена/обновлена шапка REPLACE BEFORE APPLY перед массивом

### Phase 8 (Orchestrator) — РАСШИРЕНО

Новые методы:

```csharp
public sealed class ConversionOrchestratorService : IConversionOrchestrator
{
    // ...

    /// Анализ существующего файла без модификаций.
    public AnalyzeResult Analyze(string existingTerraform);

    /// Применение/удаление шаблонного профиля.
    public ApplyProfileResult ApplyProfile(
        string existingTerraform,
        ApimTemplateProfile profile,
        ApplyProfileOptions options);

    /// Sync с автодетектом профиля.
    public SyncResult Sync(SyncRequest request);
}

public sealed record AnalyzeResult
{
    public required bool Success { get; init; }
    public DetectedProfile? DetectedProfile { get; init; }
    public List<ApimApiGroupKey> ApiGroups { get; init; } = [];
    public int TotalOperations { get; init; }
    public List<DuplicateGroup> Duplicates { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}
```

---

## §REV-2. Изменения в MCP-сервере

### REV-2.1. Общая стратегия

Существующих 6 тулов остаются (с уточнениями), плюс добавляются 3 новых. Все новые тулы используют те же сервисы из Application-слоя — никакой логики в Mcp-проекте быть не должно (правило проекта).

### REV-2.2. Новые тулы

#### A. `analyze_terraform_apim` — НОВЫЙ

**Назначение**: пользователь вставил файл, хочет понять, что в нём.

**Файл**: `src/TerraformApi.Mcp/Tools/AnalyzeTool.cs`.

**Параметры**:
| Параметр | Тип | Обязательный | Описание |
|---|---|---|---|
| `existingTerraform` | string | Да | Содержимое HCL-файла |

**Возвращает** (JSON):

```json
{
  "success": true,
  "apiGroups": [
    {
      "apimResourceGroupName": "${stage_group_name}",
      "apiName": "${api_name}-${env}",
      "operationCount": 12
    }
  ],
  "totalOperations": 12,
  "detectedProfile": {
    "confidence": "HighlyTemplated",
    "closestKnownProfileName": "UserExampleProfile",
    "fields": [
      {
        "fieldPath": "api.apim_name",
        "templatedOccurrences": 1,
        "literalOccurrences": 0,
        "observedExpressions": ["${apim_name}"]
      }
    ],
    "allReferencedVariables": [
      "stage_group_name",
      "apim_name",
      "api_name",
      "env",
      "operation_prefix"
    ]
  },
  "duplicates": [],
  "warnings": []
}
```

**Реализация**: тонкая обёртка вокруг `ConversionOrchestrator.Analyze(existingTerraform)`.

#### B. `sync_openapi_with_terraform` — НОВЫЙ (главная новая команда)

**Назначение**: основной append-only sync. Заменяет старый `update_terraform_from_openapi` в долгосрочной перспективе.

**Файл**: `src/TerraformApi.Mcp/Tools/SyncTool.cs`.

**Параметры**:
| Параметр | Тип | Обязательный | Описание |
|---|---|---|---|
| `openApiJson` | string | Нет* | OpenAPI 3.x JSON (взаимоисключающее с `openApiUrl`) |
| `openApiUrl` | string | Нет* | URL для fetch (уже поддержано через IMPLEMENTATION_SUMMARY) |
| `existingTerraform` | string | Да | Содержимое существующего HCL |
| `environment` | string | Да | `dev` / `staging` / `prod` (из ApimEnvironments) |
| `apiGroupName` | string | Да | Какой group обновлять (для disambiguation при множественных группах) |
| `templateProfileName` | string | Нет | `UserExampleProfile` / `ExtendedProfile` / `LiteralProfile` / `Auto` (default: `Auto` — детект из существующего) |
| `mergePolicyJson` | string | Нет | JSON-сериализованный `MergePolicy` для тонкой настройки |
| `matchStrategyJson` | string | Нет | JSON-сериализованный `OperationMatchStrategy` |
| `variableContextJson` | string | Нет | JSON `{"env":"dev","api_name":"bpc"}` для resolved-mode |
| `addOperationComments` | bool | Нет | Дефолт `true` |
| `addReplaceBeforeApplyHeader` | bool | Нет | Дефолт `true` |

`*` хотя бы один из `openApiJson` / `openApiUrl` должен быть задан.

**Возвращает** (JSON):

```json
{
  "success": true,
  "terraformConfig": "...полный итоговый HCL...",
  "report": {
    "operationsAdded": 2,
    "operationsPreserved": 5,
    "operationsEnriched": 1,
    "operationsIdentical": 8,
    "duplicates": [],
    "warnings": [],
    "diffs": [
      {
        "operationId": "${operation_prefix}-${env}",
        "kind": "AddedFromOpenApi",
        "fieldDiffs": []
      }
    ]
  },
  "executionGraph": { ... },
  "errors": []
}
```

#### C. `apply_template_profile` — НОВЫЙ

**Назначение**: разовая конвертация «литералов в шаблоны» или наоборот.

**Файл**: `src/TerraformApi.Mcp/Tools/ApplyTemplateProfileTool.cs`.

**Параметры**:
| Параметр | Тип | Обязательный | Описание |
|---|---|---|---|
| `existingTerraform` | string | Да | Существующий HCL |
| `direction` | string | Да | `Templatize` (lit→tmpl) или `Resolve` (tmpl→lit) |
| `profileName` | string | Только при `Templatize` | Какой профиль применить |
| `variableContextJson` | string | Только при `Resolve` | JSON значений переменных |
| `overwriteExisting` | bool | Нет | Дефолт `false` (не перезаписывать существующие литералы) |

**Возвращает**:

```json
{
  "success": true,
  "terraformConfig": "...",
  "appliedChanges": [
    "api.apim_name: \"apim-company-dev\" → ${apim_name}",
    "api_operation[1].operation_id: \"list-users-dev\" → ${operation_prefix}-${env}"
  ],
  "warnings": []
}
```

### REV-2.3. Изменения в существующих тулах

#### `convert_openapi_to_terraform` — ОБНОВЛЕНИЕ

Добавить параметры:

- `templateProfileName` (string, default `UserExampleProfile`) — какой профиль применить при генерации
- `addOperationComments` (bool, default `true`)
- `addReplaceBeforeApplyHeader` (bool, default `true`)
- `apiGroupParentPathJson` (string, default `["apis","bpc_apis","backend_apis"]`) — структура обёртки

В описание `[Description("...")]` атрибутов добавить упоминание новых возможностей.

Реализация: внутри вызвать `ConversionOrchestratorService.Convert` с `BuildOptions`, построенным из новых параметров. Старая логика — backward-compat через дефолты.

#### `update_terraform_from_openapi` — ОБНОВЛЕНИЕ (deprecated в перспективе)

Изменения:

- Внутри **делегировать в `SyncTool`** через `ConversionOrchestratorService.Sync(...)` с дефолтным `MergePolicy` (append-only) и `OperationMatchStrategy` = `[MethodAndUrl, OperationId]`.
- `templateProfileName = "Auto"` (детект из существующего файла).
- Старая сигнатура параметров сохраняется (backward-compat); все 6 unit-тестов в `UpdateToolTests` остаются зелёными.
- Добавить в `[Description]` пометку: `"Append-only merge — preserves all existing operations. For full control over policies, use sync_openapi_with_terraform."`.

#### `transform_environment` — БЕЗ ИЗМЕНЕНИЙ

Логика остаётся прежней. Но **внутри** должен использовать новый `IHclParser`/`IHclWriter` вместо текущего regex-подхода — это рефакторинг без изменения внешнего поведения. Все 8 тестов в `TransformEnvironmentToolTests` должны остаться зелёными.

#### `validate_openapi_for_apim` — БЕЗ ИЗМЕНЕНИЙ

#### `list_environment_presets` — БЕЗ ИЗМЕНЕНИЙ

#### `fetch_openapi_operations` — БЕЗ ИЗМЕНЕНИЙ

### REV-2.4. Сводная таблица MCP-тулов

| Тул                             | Статус                                   | Назначение                                        |
| ------------------------------- | ---------------------------------------- | ------------------------------------------------- |
| `fetch_openapi_operations`      | без изменений                            | Получение операций из OpenAPI URL                 |
| `validate_openapi_for_apim`     | без изменений                            | Валидация спецификации                            |
| `list_environment_presets`      | без изменений                            | Список преsets'ов                                 |
| `convert_openapi_to_terraform`  | **обновлён**                             | + `templateProfileName`, + `addOperationComments` |
| `update_terraform_from_openapi` | **обновлён (delegate)**                  | Делегирует в sync с append-only                   |
| `transform_environment`         | без изменений извне (рефакторинг внутри) | Между средами                                     |
| `analyze_terraform_apim`        | **НОВЫЙ**                                | Анализ существующего файла                        |
| `sync_openapi_with_terraform`   | **НОВЫЙ**                                | Главный sync с полной конфигурацией               |
| `apply_template_profile`        | **НОВЫЙ**                                | Templatize ↔ Resolve                              |

### REV-2.5. Регистрация в MCP-сервере

Файл: `src/TerraformApi.Mcp/Program.cs`.

В существующий `.WithToolsFromAssembly()` вызов попадут все классы, помеченные `[McpServerToolType]`. Никаких ручных регистраций не нужно — достаточно создать `AnalyzeTool`, `SyncTool`, `ApplyTemplateProfileTool` в `src/TerraformApi.Mcp/Tools/` с правильными атрибутами.

DI-зависимости новых сервисов (`IApimTemplateProfileDetector`, `IApimTemplateProfileApplier`, `IOperationCommentBuilder`, `IAppendOnlySynchronizer` и т. д.) регистрируются в `Application.DependencyInjection` и автоматически доступны MCP-инструментам.

### REV-2.6. Изменения в `.vscode/mcp.json` и `claude_desktop_config.json`

**Не требуются.** Конфиги MCP-клиентов указывают только на запуск сервера; список тулов обнаруживается динамически через `tools/list` JSON-RPC. После рестарта клиента новые тулы появляются автоматически.

### REV-2.7. Тесты MCP-слоя

К существующим 67 тестам в `tests/TerraformApi.Mcp.Tests/` добавляются:

| Файл                               | Кол-во тестов | Что покрывает                                                                                                                                                                                                                                                                                                                                                          |
| ---------------------------------- | ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AnalyzeToolTests.cs`              | 8             | Пустой файл, рабочий пример пользователя, файл с дубликатами, файл с двумя группами, MostlyLiteral, HighlyTemplated, корректные `apiGroups`, error на невалидный HCL                                                                                                                                                                                                   |
| `SyncToolTests.cs`                 | 12            | Append-only базовый, добавление 1 новой, OpenAPI пустой, обе пустые, ambiguous match, заполнение description через EnrichIfMissing, append к коллекциям request.header, проверка comments в выводе, проверка наличия REPLACE BEFORE APPLY header'а, проверка roundtrip других групп, использование `templateProfileName=Auto`, использование переопределённого профиля |
| `ApplyTemplateProfileToolTests.cs` | 6             | Templatize направление, Resolve направление, OverwriteExisting=true, OverwriteExisting=false, неизвестный профиль → ошибка, отсутствующая переменная → warning                                                                                                                                                                                                         |
| `ConvertToolTests.cs` (расширить)  | +5            | Конвертация с UserExampleProfile, с LiteralProfile, с ExtendedProfile, с `addOperationComments=false`, с custom `apiGroupParentPath`                                                                                                                                                                                                                                   |
| `UpdateToolTests.cs` (расширить)   | +3            | Update делегирует в sync с правильными дефолтами, существующие 6 тестов остаются зелёными, новые operations получают comments                                                                                                                                                                                                                                          |

**Итого**: 67 → **101 тест** в MCP-слое.

---

## §REV-3. Обновлённый Definition of Done

К пунктам §8 добавляется:

- [ ] Новые AST-типы (HclComment, HclObjectItem, HclArrayItem) — round-trip с комментариями проходит на `example-existing.tf`
- [ ] Парсер сохраняет комментарии, writer выводит их обратно с правильным отступом
- [ ] `ApimTemplateProfile.UserExampleProfile` существует и тестируется
- [ ] `ApimTemplateProfile.ExtendedProfile` существует с дополнительными плейсхолдерами из §REV-1.2.2
- [ ] `ApimTemplateProfile.LiteralProfile` существует
- [ ] `IApimTemplateProfileDetector` корректно определяет стиль на рабочем примере (Confidence=HighlyTemplated, ClosestKnownProfileName="UserExampleProfile")
- [ ] `ApisByGroupKey` в `ParsedApimDocument` корректно группирует API-операции по `(rg, api_name)`
- [ ] Каждая вставленная операция имеет 2-3 leading-комментария в формате §REV-1.5.4
- [ ] Шапка `REPLACE BEFORE APPLY` добавляется автоматически, если в файле появляются плейсхолдеры
- [ ] При синхронизации файла без переопределения профиля — новые операции автоматически получают стиль существующих
- [ ] 3 новых MCP-тула (`analyze_terraform_apim`, `sync_openapi_with_terraform`, `apply_template_profile`) зарегистрированы и работают
- [ ] Существующие MCP-тулы продолжают работать (все 67 текущих тестов зелёные)
- [ ] Добавлено 34 новых MCP-теста (см. §REV-2.7)
- [ ] В README обновлён раздел про MCP-сервер с описанием новых тулов и их параметров

---

## §REV-4. Финальный приёмочный сценарий — обновлённая версия

**Сценарий A: Sync без указания профиля (auto-detect)**

Дано:

- `existingTerraform` = рабочий пример пользователя (как есть)
- `openApiJson` = OpenAPI с операциями: GET /health (новая), GET /users (новая), GET /users/{id} (уже есть в TF)
- `apiGroupName` = `${api_group_name}` (как в файле)
- `templateProfileName` = не задан (default `Auto`)

Ожидается:

1. На выходе HCL парсится и валиден.
2. В массиве `api_operations` появилось **2** новые записи (для `/health` и `/users`), каждая со своим блоком leading-комментариев.
3. Первая строка комментария первой новой операции = ровно: `# GET /health  |  op_id: <generated_id>`.
4. Существующая операция `/users/{id}` не изменилась.
5. Используемый профиль = `UserExampleProfile` (auto-detected), новые операции содержат `${operation_prefix}-${env}`, `${stage_group_name}` и т. д.
6. Перед массивом `api_operations` присутствует или добавлен заголовочный комментарий `REPLACE BEFORE APPLY:` со всеми уникальными плейсхолдерами.
7. `SyncReport.OperationsAdded = 2`, `OperationsIdentical = 1`.

**Сценарий B: Конвертация с нуля**

Дано:

- `openApiJson` = OpenAPI с 3 операциями
- `existingTerraform` = пусто
- `templateProfileName` = `UserExampleProfile`
- `addOperationComments` = `true`

Ожидается:

1. На выходе валидный HCL со структурой `apis.bpc_apis.backend_apis."${api_group_name}" = {...}`.
2. В нём ровно 3 операции в `api_operations`.
3. Каждая операция имеет 3 leading-комментария (METHOD URL | op_id, source, placeholders).
4. Перед массивом — заголовок `REPLACE BEFORE APPLY:`.
5. Все требуемые поля (`apim_resource_group_name`, `apim_name`, `name`, `display_name`, `path`, `service_url`, `revision`) — интерполяции из профиля.

**Сценарий C: Analyze**

Дано:

- `existingTerraform` = рабочий пример пользователя

Ожидается:

1. Response.success = true
2. Response.apiGroups = 1 элемент
3. Response.totalOperations = 1 (как в примере)
4. Response.detectedProfile.confidence = `HighlyTemplated`
5. Response.detectedProfile.closestKnownProfileName = `UserExampleProfile`
6. Response.detectedProfile.allReferencedVariables содержит как минимум: `stage_group_name`, `apim_name`, `api_name`, `env`, `operation_prefix`, `operation_path`, `frontend_host`, `company_domain`, `local_dev_host`, `local_dev_port`, `api_path_prefix`, `api_path_suffix`, `api_gateway_host`, `api_version`, `backend_service_path`, `api_revision`, `product_id`, `api_display_name`, `operation_display_name`.

---

**Конец REVISION 1.**

Общий объём добавленной работы по REVISION 1: 4–6 часов сверх базового плана. Итого: **14–24 часа** для полной реализации Convert + Sync + Analyze + Profile + MCP-тулы.
