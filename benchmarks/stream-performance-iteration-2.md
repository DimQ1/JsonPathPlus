# Stream Performance Iteration 2

Date: 2026-05-19
Runtime: .NET 8.0.27
Scope: object-root `WithPaths` fast path, root-array simple-selector fast path, and cheaper scalar materialization

## Implemented changes

1. Extended the object-root `JsonElement` fast path to `ExtractAllJsonMatchesWithPathsAsync` for simple remaining segment shapes.
2. Added a root-array `JsonElement` streaming path for simple selectors so wildcard, index, range, and union cases do not deserialize every element as a full `JsonNode`.
3. Replaced `GetRawText()` plus `JsonNode.Parse()` with direct `JsonValue` creation for final `string`, `bool`, and `null` matches.

Unsupported segment types still fall back to the existing matcher path, so filters, computed selectors, recursive descent, field exclusion, and other complex shapes keep their prior behavior.

## Focused benchmark comparison

### Current vs baseline

| Method | Baseline Mean | Current Mean | Mean Delta | Baseline Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_First_SimpleProperty` | 73.74 us | 45.61 us | -38.1% | 54,616 B | 13.23 KB | -75.2% |
| `Stream_All_Range` | 866.42 us | 430.99 us | -50.3% | 485,105 B | 64.63 KB | -86.3% |
| `Stream_RootArray_First_Index` | 304.71 us | 303.96 us | -0.2% | 136,952 B | 121.38 KB | -11.4% |
| `Stream_RootArray_All_Wildcard` | 821.26 us | 665.85 us | -18.9% | 587,985 B | 336.34 KB | -42.8% |

### Current vs prior measured branch state

| Method | Prior Mean | Current Mean | Mean Delta | Prior Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_First_SimpleProperty` | 44.30 us | 45.61 us | +3.0% | 13.38 KB | 13.23 KB | -1.1% |
| `Stream_All_Range` | 396.16 us | 430.99 us | +8.8% | 66.19 KB | 64.63 KB | -2.4% |
| `Stream_RootArray_First_Index` | 309.85 us | 303.96 us | -1.9% | 121.54 KB | 121.38 KB | -0.1% |
| `Stream_RootArray_All_Wildcard` | 815.50 us | 665.85 us | -18.4% | 413.76 KB | 336.34 KB | -18.7% |

## Validation

1. Stream extraction test file: passed (`240/240`)
2. Focused BenchmarkDotNet run: passed
3. Artifacts written to `artifacts/iteration2_benchmarks/results/`

## Notes

1. The root-array wildcard case responded strongly to the cheaper final materialization step, which indicates the remaining cost is not only traversal but also how matched scalars are turned back into `JsonNode`.
2. Object-root `WithPaths` is now on the fast path for the supported segment set, but there is still no dedicated benchmark covering that API shape.
3. The object-root no-path benchmarks stayed far ahead of baseline, but this iteration traded a small amount of mean time for a small allocation drop in those already-optimized cases.

## Next candidates

1. Add `WithPaths` benchmarks for both object-root and root-array stream selectors.
2. Remove `segments.Skip(1).ToList()` allocations via start-index traversal.
3. Investigate a numeric-scalar materialization fast path that preserves JSON semantics.
4. Re-measure before expanding optimization to root-array filter handling.