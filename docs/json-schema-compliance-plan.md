# JsonPathPlus JSON Schema — Gap Analysis vs json-schema.org (2020-12)

## Current State

`JsonPathSchemaGenerator` (`JsonPathSchemaGenerator.cs`, ~270 строк) — это **data-driven schema inference engine**, который строит JSON Schema из `JsonNode` (образца данных). Заявленная совместимость: **draft-07**.

### Что уже реализовано

| Возможность | Ключевые слова | Статус |
|---|---|---|
| Базовые типы | `type` (строка) | ✅ `string`, `number`, `boolean`, `null`, `object`, `array` |
| Свойства объектов | `properties`, `required` | ✅ |
| Схема элементов массива | `items` | ✅ (merged) |
| Объединение типов | `oneOf` | ✅ (для mixed arrays/objects) |
| Слияние схем | MergeSchemas | ✅ (object merge, array merge, primitive merge) |

### Что НЕ реализовано — полный список по спецификации 2020-12

---

## 1. Core Vocabulary (обязательный, `$`-префиксные ключи)

| Ключ | Статус | Применимость к data-driven генератору |
|---|---|---|
| `$schema` | ❌ | ✅ Можно генерировать URI диалекта |
| `$id` | ❌ | ✅ Можно генерировать |
| `$ref` | ❌ | ✅ Для ссылок на `$defs` |
| `$defs` | ❌ | ✅ Для reusable definitions |
| `$anchor` | ❌ | ⚠️ Опционально |
| `$dynamicRef` | ❌ | ⚠️ Редко нужно в автогенерации |
| `$dynamicAnchor` | ❌ | ⚠️ Редко нужно |
| `$comment` | ❌ | ✅ Можно добавить опционально |
| `$vocabulary` | ❌ | ✅ Декларация используемых vocabulary |

---

## 2. Applicator Vocabulary (применение подсхем)

| Ключ | Статус | Применимость |
|---|---|---|
| `allOf` | ❌ | ✅ Объединение ограничений из разных источников |
| `anyOf` | ❌ | ✅ Альтернативные схемы |
| `not` | ❌ | ⚠️ Ограниченная применимость в автогенерации |
| `if`/`then`/`else` | ❌ | ⚠️ Условная логика требует семантики |
| `properties` | ✅ (частично) | ❌ Нет `additionalProperties`, нет схемы для значений |
| `patternProperties` | ❌ | ✅ Для объектов с паттернами ключей |
| `additionalProperties` | ❌ | ✅ Контроль дополнительных свойств |
| `propertyNames` | ❌ | ⚠️ Ограниченная применимость |
| `minProperties`/`maxProperties` | ❌ | ✅ Извлекается из данных |
| `unevaluatedProperties` | ❌ | ✅ Важно для строгой валидации |
| `prefixItems` (tuple) | ❌ | ✅ Для массивов с фиксированной структурой |
| `items` | ✅ (частично) | ❌ Нет `additionalItems`, нет tuple-формы |
| `contains` | ❌ | ✅ Проверка наличия элемента |
| `minContains`/`maxContains` | ❌ | ✅ Количество совпадений |
| `unevaluatedItems` | ❌ | ✅ Контроль неоценённых элементов |
| `dependentSchemas` | ❌ | ✅ Зависимости между свойствами |

---

## 3. Validation Vocabulary (структурная валидация)

| Ключ | Статус | Применимость |
|---|---|---|
| `type` (string) | ✅ | ❌ Нет `"integer"` типа |
| `type` (array) | ❌ | ✅ Для nullable: `["string", "null"]` |
| `enum` | ❌ | ✅ Когда мало уникальных значений |
| `const` | ❌ | ✅ Когда значение всегда одно |
| `multipleOf` | ❌ | ⚠️ Редко выводится из данных |
| `minimum`/`maximum` | ❌ | ✅ Из диапазона чисел |
| `exclusiveMinimum`/`exclusiveMaximum` | ❌ | ✅ Опционально |
| `maxLength`/`minLength` | ❌ | ✅ Из длин строк |
| `pattern` | ❌ | ✅ Обнаружение паттернов строк |
| `maxItems`/`minItems` | ❌ | ✅ Из размеров массивов |
| `uniqueItems` | ❌ | ✅ Если все элементы уникальны |
| `dependentRequired` | ❌ | ✅ Из корреляции свойств |

---

## 4. Format Vocabulary (семантические форматы)

| Ключ | Статус | Применимость |
|---|---|---|
| `format` | ❌ | ✅ Автоопределение (date-time, email, uri, ipv4 и т.д.) |

Форматы: `date-time`, `date`, `time`, `duration`, `email`, `idn-email`, `hostname`, `idn-hostname`, `ipv4`, `ipv6`, `uri`, `uri-reference`, `iri`, `iri-reference`, `uuid`, `uri-template`, `json-pointer`, `relative-json-pointer`, `regex`

---

## 5. Content Vocabulary

| Ключ | Статус | Применимость |
|---|---|---|
| `contentEncoding` | ❌ | ⚠️ Только если данные base64-encoded |
| `contentMediaType` | ❌ | ⚠️ Только с доп. контекстом |
| `contentSchema` | ❌ | ⚠️ Редко нужно в автогенерации |

---

## 6. Meta-Data Vocabulary (аннотации)

| Ключ | Статус | Применимость |
|---|---|---|
| `title` | ❌ | ✅ Опциональная аннотация |
| `description` | ❌ | ✅ Опциональная аннотация |
| `default` | ❌ | ✅ Из примеров данных |
| `deprecated` | ❌ | ⚠️ Требует семантики |
| `readOnly` | ❌ | ⚠️ Требует семантики |
| `writeOnly` | ❌ | ⚠️ Требует семантики |
| `examples` | ❌ | ✅ Из примеров данных |

---

## 7. Unevaluated Vocabulary

| Ключ | Статус | Применимость |
|---|---|---|
| `unevaluatedProperties` | ❌ | ✅ (см. Applicator) |
| `unevaluatedItems` | ❌ | ✅ (см. Applicator) |

---

## Итого

- **Реализовано**: ~5 ключей (type, properties, required, items, oneOf)
- **Спецификация 2020-12**: **~60+ ключей** в 7 vocabulary
- **Применимо к data-driven генератору**: **~40+ ключей**
- **Текущее покрытие**: **~8% от применимых ключей**

---

# План полной поддержки спецификации

План разбит на 6 фаз по возрастанию сложности и ценности. Каждая фаза — независимый PR/MR.

## Фаза 1: Foundation — переход на 2020-12 dialect

**Цель:** Генератор выдаёт схему с правильным `$schema`, `$id`, `$vocabulary`, `$defs`.

- [ ] Добавить `JsonPathSchemaGenerationOptions`:
  - `SchemaDialect` (enum: Draft07, Draft202012)
  - `SchemaId` (string?)
  - `IncludeVocabularies` (bool, default true)
  - `IncludeDefs` (bool, default false)
- [ ] Генерировать `$schema`: `"https://json-schema.org/draft/2020-12/schema"`
- [ ] Генерировать `$vocabulary` с правильными URI
- [ ] Поддержка `$defs` для выноса повторяющихся схем объектов
- [ ] Генерировать `$id` при наличии опции
- [ ] Генерировать `$comment` опционально
- [ ] `type` как массив: `["string", "null"]` для nullable
- [ ] Тип `"integer"` когда все числа целые

## Фаза 2: Validation Constraints — числовые и строковые ограничения

**Цель:** Генератор извлекает min/max/pattern из данных.

- [ ] `minimum`/`maximum` — из диапазона чисел
- [ ] `exclusiveMinimum`/`exclusiveMaximum` — опционально
- [ ] `multipleOf` — с опцией обнаружения
- [ ] `minLength`/`maxLength` — из длин строк
- [ ] `pattern` — автоопределение распространённых паттернов (email, uuid, ip, даты, только цифры, только буквы)
- [ ] `minItems`/`maxItems` — из размеров массивов
- [ ] `minProperties`/`maxProperties` — из количества свойств объектов
- [ ] `uniqueItems: true` — когда все элементы массива уникальны

## Фаза 3: Enum & Const — точные значения

**Цель:** Генератор находит enum и const из повторяющихся данных.

- [ ] `const` — когда все сэмплы одного значения одинаковы
- [ ] `enum` — когда уникальных значений ≤ N (опция `MaxEnumValues`, default 10)
- [ ] `enum` + `type` комбинация для typed enum'ов
- [ ] Опция `InferConst` (bool, default true)
- [ ] Опция `InferEnum` (bool, default true)

## Фаза 4: Format Auto-Detection — семантические форматы

**Цель:** Генератор автоматически определяет `format` для строк.

- [ ] `format: "date-time"` — ISO 8601
- [ ] `format: "date"` — YYYY-MM-DD
- [ ] `format: "time"` — HH:MM:SS
- [ ] `format: "email"` — содержит @
- [ ] `format: "ipv4"` — x.x.x.x
- [ ] `format: "ipv6"` — IPv6 pattern
- [ ] `format: "uri"` — начинается с scheme://
- [ ] `format: "uuid"` — UUID pattern
- [ ] `format: "hostname"` — hostname-like
- [ ] `format: "json-pointer"` — начинается с /
- [ ] `format: "regex"` — содержит regex-подобные символы
- [ ] Опция `InferFormat` (bool, default true)
- [ ] Опция `FormatConfidence` (enum: Low/Medium/High, default Medium) — порог уверенности

## Фаза 5: Applicator Logic — продвинутая композиция схем

**Цель:** Генератор использует `allOf`/`anyOf`/`if-then-else`/`dependentSchemas`.

- [ ] `allOf` — когда несколько независимых ограничений
- [ ] `anyOf` — альтернативные схемы (замена `oneOf` где уместно)
- [ ] `not` — для исключения конкретных паттернов
- [ ] `additionalProperties: false` — строгий режим
- [ ] `additionalProperties: { schema }` — с разрешённой схемой
- [ ] `patternProperties` — для объектов с ключами-паттернами
- [ ] `propertyNames` — ограничения на имена свойств
- [ ] `dependentRequired` — если свойство A → требуется свойство B
- [ ] `dependentSchemas` — если свойство A → применяется схема B
- [ ] `prefixItems` (tuple validation) — для массивов с фиксированной позиционной структурой
- [ ] `contains` — проверка наличия элемента определённого типа
- [ ] `minContains`/`maxContains` — границы количества совпадений
- [ ] `unevaluatedProperties: false` — строгий режим
- [ ] `unevaluatedItems: false` — строгий режим

## Фаза 6: Meta-Data & Content — аннотации и документация

**Цель:** Добавление title/description/default/examples/content*.

- [ ] `title` — опциональная генерация из имени свойства
- [ ] `description` — опционально
- [ ] `default` — из первого/самого частого значения
- [ ] `examples` — коллекция примеров
- [ ] `deprecated` — только через опцию (ручная пометка)
- [ ] `readOnly`/`writeOnly` — только через опцию
- [ ] `contentEncoding` — автоопределение base64
- [ ] `contentMediaType` — автоопределение (JSON-in-string, XML-in-string)
- [ ] `contentSchema` — для JSON-in-string

---

# Приоритеты и дорожная карта

```
Фаза 1 (Foundation)        ████████████████  Неделя 1-2
Фаза 2 (Validation)        ████████████████  Неделя 3-4
Фаза 3 (Enum/Const)        ████████████      Неделя 4-5
Фаза 4 (Format Detection)  ████████████      Неделя 5-6
Фаза 5 (Applicator)        ██████████████████ Неделя 6-9
Фаза 6 (Meta-Data)         ████████          Неделя 9-10
```

**Общая оценка:** ~10 недель на полную реализацию.

---

# Ключевые архитектурные решения

1. **Новый класс `JsonPathSchemaGenerationOptions`** — все опции генерации в одном месте.
2. **Обратная совместимость** — текущий API без опций должен продолжать работать (draft-07 по умолчанию; флаг для перехода на 2020-12).
3. **Инкрементальное слияние** — текущий `MergeSchemas` расширяется для поддержки всех applicator-ключей.
4. **Тестирование** — каждый новый ключ покрывается unit-тестами в `JsonPathSchemaExtractionTests.cs`.
5. **Format detection через стратегии** — каждая проверка формата — отдельный метод/класс, цепочка responsibility.
