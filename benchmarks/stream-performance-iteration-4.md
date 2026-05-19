# Stream Performance Iteration 4

Date: 2026-05-19
Runtime: .NET 8.0.27
Scope: replace root-array simple-selector `DeserializeAsyncEnumerable<JsonElement>` traversal with a `Utf8JsonReader` byte scan

## Implemented changes

1. Switched the root-array simple-selector fast path from `JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream)` to a `Utf8JsonReader` loop over the stream bytes.
2. Parse matched array items on demand with `JsonDocument.ParseValue(ref reader)` and evaluate the remaining selector directly on `JsonElement`.
3. Kept the existing JsonNode-based fallback path unchanged for filters, negative indices, field exclusion, and unsupported selector shapes.

This change keeps selector semantics the same while removing the async deserializer overhead from the simple root-array fast path.

## Focused benchmark comparison

### Current vs baseline

| Method | Baseline Mean | Current Mean | Mean Delta | Baseline Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_RootArray_First_Index` | 304.71 us | 42.17 us | -86.2% | 136,952 B | 32.70 KB | -75.5% |
| `Stream_RootArray_All_Wildcard` | 821.26 us | 503.08 us | -38.7% | 587,985 B | 236.16 KB | -59.8% |

### Current vs iteration 3

| Method | Iteration 3 Mean | Current Mean | Mean Delta | Iteration 3 Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_RootArray_First_Index` | 307.47 us | 42.17 us | -86.3% | 121.16 KB | 32.70 KB | -73.0% |
| `Stream_RootArray_All_Wildcard` | 640.71 us | 503.08 us | -21.5% | 336.12 KB | 236.16 KB | -29.7% |

## Validation

1. Targeted root-array stream tests: passed (`24/24`)
2. Focused BenchmarkDotNet run: passed
3. Artifacts written to `artifacts/iteration4_root_array/results/`

## Notes

1. The prior root-array fast path was still paying a large per-element cost in `DeserializeAsyncEnumerable<JsonElement>`. Removing that overhead shifted the first-index case from roughly `304 us` to `42 us`.
2. Wildcard also improved materially, but less dramatically, because it still pays per-match materialization and path traversal costs after the array element is located.
3. The unchanged fallback path means filter, negative-index, and field-exclusion behavior remain on the prior implementation until there is benchmark coverage and a separate optimization pass for those shapes.

## Next candidates

1. Add dedicated `WithPaths` benchmarks for object-root and root-array stream selectors.
2. Add a numeric-scalar materialization fast path if it can preserve JSON number formatting semantics.
3. Revisit root-array filter handling against the new root-array baseline.