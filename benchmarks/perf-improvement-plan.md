# План улучшений производительности — реализация

**Создан:** 2026-06-25
**Завершён:** 2026-06-25
**Стратегия:** каждая оптимизация в отдельной ветке → тесты → мерж в main если эффект есть, иначе пропуск.

## Итоги

| # | Название | Ветка | Статус | Эффект |
|---|----------|-------|--------|--------|
| 1 | #12: Cache JsonValue.Create(true/false) | `perf/cache-jsonvalue` | ❌ Пропущено (небезопасно — JsonNode.Parent мутабелен) | — |
| 2 | #10: Fast operand type check | `perf/fast-operand-check` | ✅ Merged | ⚡ Избегает decimal.TryParse для не-чисел |
| 3 | #14: Linear scan для ≤3 полей исключения | `perf/linear-exclusion` | ✅ Merged | ⚡ Нет аллокации HashSet для типичных случаев |
| 4 | #16: Replace record struct | — | ❌ Пропущено (минимальный эффект) | — |
| 5 | #15: long position в ReadOnlyMemoryStream | `perf/long-position` | ✅ Merged | ⚡ Корректность + нет каста |
| 6 | #6: List pooling в FindSegmentMatches | `perf/list-pooling` | ✅ Merged | ⚡⚡⚡ N+1 → 2 аллокации списков |
| 7 | #11: Pattern-matching Compare | — | ❌ Пропущено (ToString на string — no-op) | — |
| 8 | #9: ReadOnlySpan в FilterEvaluator | `perf/filter-span` | ✅ Merged | ⚡⚡ Нет подстрок при рекурсивных ||/&&/! |
| 9 | #7: Span-based union parsing | `perf/span-union` | ✅ Merged | ⚡⚡ Нет ToString().Split() для union/exclusion/projection |
| 10 | #8: Span-based expression evaluation | `perf/span-expression` | ✅ Merged | ⚡⚡ Нет 4×Replace + Split для computed expressions |

**Результат:** 7 оптимизаций смержено, 3 пропущено. Все 1356 тестов проходят на net8.0–net11.0.

⚡ = малый эффект, ⚡⚡ = средний эффект, ⚡⚡⚡ = большой эффект
