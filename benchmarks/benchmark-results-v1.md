# JsonPathPlus Benchmark Results — Baseline v1

**Date:** 2026-05-19  
**BenchmarkDotNet:** v0.14.0  
**Runtime:** .NET 8.0.27, X64 RyuJIT AVX2  
**GC:** Concurrent Workstation  
**OS:** Windows 11

---

## Summary Table (Performance & Memory)

| Method | Mean | Allocated | Gen0/1k | Gen1/1k | Gen2/1k |
|---|---|---|---|---|---|
| **Stream — ExtractFirstJsonMatchAsync** | | | | | |
| `Stream_First_SimpleProperty` ($.items[0].name, 100 items) | 73.7 μs | 54,616 B | 8.7 | 0.98 | — |
| `Stream_First_ArrayIndex` ($.items[500].name, 1000 items) | 809.7 μs | 478,881 B | 71.3 | 71.3 | 71.3 |
| `Stream_First_Wildcard` ($.items[*].name, 1000 items) | 1,267.3 μs | 1,055,273 B | 142.6 | 70.3 | 70.3 |
| `Stream_First_RecursiveDescent` ($.a..target, deeply nested) | 4.8 μs | 11,408 B | 1.8 | 0.02 | — |
| **Stream — ExtractAllJsonMatchesAsync** | | | | | |
| `Stream_All_Wildcard` ($.items[*].id, 1000 items) | 1,640.0 μs | 1,072,397 B | 140.6 | 70.3 | 70.3 |
| `Stream_All_Range` ($.items[0:10].name, 1000 items) | 866.4 μs | 485,105 B | 71.3 | 71.3 | 71.3 |
| **Stream — with JsonPathExtractionOptions** | | | | | |
| `StreamOptions_First` (default options) | 75.4 μs | 54,544 B | 8.7 | 0.98 | — |
| `StreamOptions_All` (default options) | 79.6 μs | 60,680 B | 9.6 | 0.73 | — |
| `StreamOptionsCapped_First` (FullParseMaxBytes=10M) | 72.9 μs | 54,544 B | 8.7 | 0.98 | — |
| `StreamOptionsCapped_All` (FullParseMaxBytes=10M) | 85.1 μs | 60,680 B | 9.6 | 0.73 | — |
| **JsonNode — ExtractFirstJsonMatchAsync** | | | | | |
| `Node_First_SimpleProperty` | **435 ns** ⚡ | 1,040 B | 0.17 | — | — |
| `Node_First_NestedProperty` (5 levels deep) | 686 ns | 1,928 B | 0.31 | 0.001 | — |
| `Node_First_Root` (null selector) | **12.3 ns** ⚡ | 104 B | 0.02 | — | — |
| **JsonNode — ExtractAllJsonMatchesAsync** | | | | | |
| `Node_All_Wildcard` ($.items[*].id, 1000 items) | 69.0 μs | 34,136 B | 5.4 | 0.24 | — |
| `Node_All_Range` ($.items[0:10].name) | 1.5 μs | 1,680 B | 0.27 | — | — |
| **string — ExtractFirstJsonMatchAsync** | | | | | |
| `String_First_SimpleProperty` | 30.1 μs | 26,640 B | 4.2 | 0.27 | — |
| `String_First_ArrayIndex` ($.items[500].name) | 352.0 μs | 249,753 B | 36.6 | 36.6 | 36.6 |
| `String_First_RecursiveDescent` | 2.6 μs | 3,656 B | 0.58 | 0.004 | — |
| `String_First_Root` (null selector) | 327.2 μs | 175,929 B | 36.6 | 36.6 | 36.6 |
| **string — ExtractAllJsonMatchesAsync** | | | | | |
| `String_All_Wildcard` ($.items[*].id, 1000 items) | 854.0 μs | 826,298 B | 117.2 | 78.1 | 35.2 |
| `String_All_Range` ($.items[0:10].name) | 37.2 μs | 32,176 B | 5.1 | 0.24 | — |
| **Stream — Root array (streaming path)** | | | | | |
| `Stream_RootArray_First_Index` ($[250].name, 500 items) | 304.7 μs | 136,952 B | 21.5 | 4.4 | — |
| `Stream_RootArray_All_Wildcard` ($[*].name, 500 items) | 821.3 μs | 587,985 B | 92.8 | 23.4 | — |

---

## Key Insights

### 1. Input type hierarchy (fastest → slowest for same path)
- **JsonNode** → **string** → **Stream**
- `Node_First_SimpleProperty`: 435 ns (no parse overhead)
- `String_First_SimpleProperty`: 30.1 μs (~69× slower than JsonNode, parsing overhead)
- `Stream_First_SimpleProperty`: 73.7 μs (~169× slower than JsonNode, stream + parse)

### 2. Memory efficiency
| Input | First SimpleProperty (alloc) | Root/null (alloc) |
|---|---|---|
| JsonNode | **1,040 B** | **104 B** |
| string | 26,640 B | 175,929 B |
| Stream | 54,616 B | varies |

JsonNode paths are **52× more memory-efficient** than Stream for simple lookups.

### 3. Options overhead
- `JsonPathExtractionOptions` adds negligible overhead (~2 μs for First, ~6 μs for All)
- `FullParseMaxBytes` cap has no measurable cost when set high enough

### 4. Wildcard cost on large arrays (1000 items)
- `Stream_All_Wildcard`: 1,640 μs / 1,072,397 B — **highest cost overall**
- `Node_All_Wildcard`: 69 μs / 34,136 B — **24× faster, 31× less memory**

### 5. Recursive descent is fast
- `Stream_First_RecursiveDescent`: 4.8 μs / 11,408 B
- `Node_First_NestedProperty` (explicit 5-level): 686 ns / 1,928 B

### 6. GC pressure (Stream vs JsonNode on large arrays)
- Stream wildcard on 1000 items: **Gen2 GC** triggers (70.3 per 1k ops)
- JsonNode wildcard on 1000 items: only Gen0 (5.4 per 1k ops), **no Gen2**

### 7. Streaming optimization (root arrays)
- `Stream_RootArray_First_Index` (500 items): 304.7 μs — uses streaming path
- `Stream_First_ArrayIndex` (1000 items): 809.7 μs — full parse fallback
- Root array streaming is ~2.7× faster per item

---

## Recommendations
1. **Prefer JsonNode overloads** when JSON is already parsed — up to **169× faster**
2. **Use string overloads** for one-shot parsing + extraction
3. **Stream overloads** are best for large documents that can't fit in memory
4. **Avoid wildcard on large arrays via Stream** — causes heavy Gen2 GC
5. **Root-level arrays** benefit from streaming optimization (2.7× improvement)

---

*This file is the baseline for future comparisons. Any performance improvements should be measured against these numbers.*
