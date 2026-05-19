# Stream Performance Iteration 3

Date: 2026-05-19
Runtime: .NET 8.0.27
Scope: remove streaming-path `segments.Skip(1).ToList()` allocations via segment start-index traversal

## Implemented changes

1. Added start-index overloads to `JsonPathExtractionCore` and `JsonPathMatcher` so callers can traverse a suffix of the parsed segment list without allocating a new list.
2. Updated all array-root and object-root streaming fallback paths to call the new start-index overloads instead of creating `remainingSegments` copies.
3. Updated the `JsonElement` fast-path helpers to walk the original segment list from a supplied start index instead of receiving a copied tail list.

This change is mechanical and behavior-preserving. It does not alter selector semantics; it only removes per-call list allocations from the streaming path.

## Focused benchmark comparison

### Current vs baseline

| Method | Baseline Mean | Current Mean | Mean Delta | Baseline Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_First_SimpleProperty` | 73.74 us | 46.03 us | -37.6% | 54,616 B | 12.95 KB | -75.7% |
| `Stream_All_Range` | 866.42 us | 437.43 us | -49.5% | 485,105 B | 64.34 KB | -86.4% |
| `Stream_RootArray_First_Index` | 304.71 us | 307.47 us | +0.9% | 136,952 B | 121.16 KB | -11.5% |
| `Stream_RootArray_All_Wildcard` | 821.26 us | 640.71 us | -22.0% | 587,985 B | 336.12 KB | -42.8% |

### Current vs iteration 2

| Method | Iteration 2 Mean | Current Mean | Mean Delta | Iteration 2 Alloc | Current Alloc | Alloc Delta |
|---|---:|---:|---:|---:|---:|---:|
| `Stream_First_SimpleProperty` | 45.61 us | 46.03 us | +0.9% | 13.23 KB | 12.95 KB | -2.1% |
| `Stream_All_Range` | 430.99 us | 437.43 us | +1.5% | 64.63 KB | 64.34 KB | -0.4% |
| `Stream_RootArray_First_Index` | 303.96 us | 307.47 us | +1.2% | 121.38 KB | 121.16 KB | -0.2% |
| `Stream_RootArray_All_Wildcard` | 665.85 us | 640.71 us | -3.8% | 336.34 KB | 336.12 KB | -0.1% |

## Validation

1. Stream extraction test file: passed (`240/240`)
2. Focused BenchmarkDotNet run: passed
3. Artifacts written to `artifacts/iteration3_benchmarks/results/`

## Notes

1. The start-index traversal change is primarily an allocation cleanup. Its measured runtime effect is small on most cases, which is consistent with the removed work being list-copy overhead rather than the dominant traversal cost.
2. The clearest runtime improvement in this pass is `Stream_RootArray_All_Wildcard`, which improved again while staying flat on allocation.
3. The object-root benchmarks remain far ahead of baseline, but this iteration did not materially move their latency; the next meaningful wins likely require reducing match materialization or expanding fast paths rather than further trimming list overhead.

## Next candidates

1. Add dedicated `WithPaths` benchmarks for object-root and root-array stream selectors.
2. Add a numeric-scalar materialization fast path if it can preserve JSON number formatting semantics.
3. Revisit root-array filter handling after the `WithPaths` benchmark coverage is in place.