# Stream Performance Iteration 1

Date: 2026-05-19
Runtime: .NET 8.0.27
Scope: object-root stream selectors with simple property/index/range traversal

## Implemented changes

1. Removed the raw-text reparse from the object-root stream path.
2. Replaced the extra `MemoryStream` plus `ToArray()` copy with a direct exact-size byte read for seekable streams.
3. Added a `JsonElement` fast path for simple remaining segment shapes after the first root-object property match:
   - `Property`
   - `ArrayIndex`
   - `ArrayRange`
   - `Wildcard`

Unsupported segment types still fall back to the existing `JsonNode` matcher, so behavior stays unchanged for filters, computed indices, recursive descent, projections, and unions.

## Focused benchmark comparison

| Method | Baseline Mean | Current Mean | Mean Delta | Baseline Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_First_SimpleProperty` | 73.74 us | 44.30 us | -39.9% | 54,616 B | 13.38 KB | -74.9% |
| `Stream_First_ArrayIndex` | 809.66 us | 447.22 us | -44.8% | 478,881 B | 62.5 KB | -86.6% |
| `Stream_All_Range` | 866.42 us | 396.16 us | -54.3% | 485,105 B | 66.19 KB | -86.0% |

## Validation

- Stream extraction test slice: passed (`240/240`)
- Focused BenchmarkDotNet run: passed

## Notes

The biggest gain came from avoiding full `JsonNode` materialization of the matched root-object property value when the remaining selector can be evaluated directly on `JsonElement`.

## Next candidates

1. Extend the same `JsonElement` fast path to `ExtractAllJsonMatchesWithPathsAsync` for object-root selectors.
2. Apply the same segment-walking optimization to root-array streaming instead of `DeserializeAsyncEnumerable<JsonNode>`.
3. Remove `segments.Skip(1).ToList()` allocations by switching to segment start-index traversal.
