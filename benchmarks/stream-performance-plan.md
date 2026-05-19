# Stream Performance Improvement Plan

**Date:** 2026-05-19  
**Based on:** `benchmark-results-v1.md`  
**Goal:** Reduce Stream latency by 5–10× and memory by 3–5× for common paths

---

## Status Update — Iterations 1-2

### Completed work

1. Eliminated the extra raw-text reparse on the object-root stream path for simple selectors.
2. Extended the same `JsonElement` fast path to `ExtractAllJsonMatchesWithPathsAsync` for object-root selectors.
3. Applied a `JsonElement`-based fast path to root-array streaming for simple wildcard/index/range/union selectors while preserving the existing fallback for unsupported segment types.
4. Avoided `GetRawText()` plus `JsonNode.Parse()` for final `string`, `bool`, and `null` matches by materializing those scalars directly.

### Measured outcome so far

1. Object-root simple and range selectors remain materially improved versus baseline, with roughly 38–50% lower mean time and 75–86% lower allocation.
2. Root-array wildcard is now materially better than baseline at both latency and allocation.
3. Root-array first-index is effectively flat on latency versus baseline, but uses less memory.

### Revised next priorities

1. Add dedicated `WithPaths` benchmarks for object-root and root-array streaming so the new path-aware fast path has its own baseline.
2. Remove `segments.Skip(1).ToList()` allocations by switching the matcher to segment start-index traversal.
3. Reduce the remaining `MaterializeElement` cost for numeric scalars if that can be done without changing JSON semantics.
4. Revisit root-array filter and other unsupported first-segment shapes only after the benchmark coverage above is in place.

---

## 1. Root Cause Analysis

### Benchmark data tells the story

| Benchmark | Mean | Allocated | GC Gen2/1k | What's happening |
|---|---|---|---|---|
| `Node_First_SimpleProperty` | **435 ns** | 1,040 B | 0 | Pre-parsed tree walk |
| `String_First_SimpleProperty` | 30.1 μs | 26,640 B | 0 | One parse + tree walk |
| `Stream_First_SimpleProperty` | **73.7 μs** | 54,616 B | 0 | Stream→full parse→tree walk |
| `Stream_First_ArrayIndex` | 809.7 μs | 478,881 B | **Gen2: 71.3** | Full 1000-element parse |
| `Stream_All_Wildcard` | 1,640 μs | 1,072,397 B | **Gen2: 70.3** | Worst case — full parse + materialize all |

**The gap:** Stream is **169× slower** than JsonNode for the same path. The parse step dominates.

### What the code actually does for `$.items[0].name` (Stream)

```
ExtractFirstJsonMatchAsync(stream, "$.items[0].name")
  → ParseSegments → [Property("items"), ArrayIndex(0), Property("name")]
  → CanUseStreaming? Root is '{' (object), segments[0] is Property("items")
    → Object root + Property first segment → SHOULD return true? NO!
    → Look at CanUseStreaming for Object root:
      ✅ Property → true
      ✅ Wildcard → true  
      ✅ PropertyUnion → true
    → Wait, it SHOULD return true for `$.items[0].name`!
```

**Wait — re-examining the code:**

```csharp
// In CanUseStreaming:
RootContainerKind.Object => first.SegmentType switch
{
    Property => !string.IsNullOrWhiteSpace(first.PropertyName),  // ✅ "items"
    Wildcard => true,
    PropertyUnion => first.PropertyUnionNames is { Length: > 0 },
    _ => false
}
```

So `CanUseStreaming` DOES return `true` for `$.items[0].name` with a root object. The streaming path IS used.

**But then what happens in `ExtractFirstObjectRootMatch`?**

```csharp
// It reads the ENTIRE stream to a byte[]:
var bytes = await ReadToEndAsync(stream);  // ← MEMORY COPY!

// Then walks the object with Utf8JsonReader
// For each matching property, it does:
using var propertyDocument = JsonDocument.ParseValue(ref reader);     // ← PARSE #1
var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText()); // ← PARSE #2!
var match = JsonPathExtractionCore.FindFirstMatch(propertyNode, remainingSegments);
```

**THERE IT IS:** The object-root streaming path:
1. Copies the ENTIRE stream to `byte[]` (ReadToEndAsync)
2. For `items` property, parses the value TWICE (JsonDocument.ParseValue + JsonNode.Parse)  
3. The `items` value is a 100-element array → 54KB of raw JSON → parsed fully into a JsonArray tree

**And for the wildcard benchmarks** (e.g., `$.items[*].name`):
- Root is `{`, so object streaming path is used
- Finds `items` property
- Double-parses the 1000-element array into a full JsonArray
- Then `JsonPathMatcher.FindMatches` walks all 1000 items to extract `name`

### The true bottleneck summary

| Problem | Location | Severity |
|---|---|---|
| **`ReadToEndAsync` copies entire stream** | `ExtractFirstObjectRootMatch` | 🔴 Critical — double memory |
| **Double parse** (`JsonDocument` → `JsonNode`) | `ExtractFirstObjectRootMatch` | 🔴 Critical — 2× parse time |
| **Full nested array materialization** | `JsonPathExtractionCore.FindFirstMatch` on full `JsonArray` | 🔴 Critical — wildcard/range on large arrays |
| **`segments.Skip(1).ToList()` allocation** | Every streaming method | 🟡 Medium — per-call alloc |
| **`JsonPathMatcher.FindMatches` creates intermediate lists** | `FindSegmentMatches` | 🟡 Medium — segment-level alloc |
| **`DeserializeAsyncEnumerable<JsonNode>` is heavy** | Root-array path | 🟡 Medium — full deserialization per element |
| **`GetRootContainerKind` creates a `StreamReader`** | Every `CanUseStreaming` call | 🟢 Minor — but called multiple times |

---

## 2. Improvement Plan (Prioritized)

### 🔴 Priority 1: Eliminate `ReadToEndAsync` in object-root path

**Today:**
```csharp
var bytes = await ReadToEndAsync(stream);  // Copies entire stream to byte[]
```
**Problem:** For a 100-item JSON (~5KB), this doubles memory. For 1000 items (~50KB), it's 50KB × 2 = 100KB.

**Fix:** Process directly from the stream using pipe-based `Utf8JsonReader` or `System.IO.Pipelines`.
Keep the stream open, read chunks, and match lazily.

**Expected impact:** 
- Memory: **−50%** (no duplicate byte[])
- Speed: **−10–15%** (no copy overhead)

**Implementation approach:**
```csharp
// Instead of ReadToEndAsync, use a pooled buffer:
byte[] buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
try {
    await stream.ReadExactlyAsync(buffer, 0, (int)stream.Length);
    // Use Utf8JsonReader on the buffer directly
} finally {
    ArrayPool<byte>.Shared.Return(buffer);
}
```
Or better: stream directly via `PipeReader`.

---

### 🔴 Priority 2: Kill the double parse — pass JsonElement, not JsonNode

**Today (in object-root path):**
```csharp
using var propertyDocument = JsonDocument.ParseValue(ref reader);     // Parse #1
var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText()); // Parse #2 + alloc
var match = JsonPathExtractionCore.FindFirstMatch(propertyNode, remainingSegments);
```

**Problem:** `JsonDocument.ParseValue` parses the JSON value. Then `GetRawText()` serializes it back to string. Then `JsonNode.Parse` parses it AGAIN. This is 2× parse + string allocation.

**Fix:** Create a `JsonElement`-based matching path. `JsonElement` is the lightweight parsed representation from `JsonDocument`. Create an overload of `JsonPathExtractionCore.FindFirstMatch` that accepts `JsonElement`.

```csharp
using var propertyDocument = JsonDocument.ParseValue(ref reader);
var match = JsonPathExtractionCore.FindFirstMatchElement(
    propertyDocument.RootElement, remainingSegments);
```

**Expected impact:**
- Speed: **−30–40%** for object-root paths (one parse instead of two)
- Memory: **−string allocation** (~5–50KB per call)

**Note:** Requires a parallel `JsonElement`-based matching infrastructure alongside the existing `JsonNode` one. Can start with `FindFirstMatch` and `FindAllMatches`.

---

### 🔴 Priority 3: Lazy matching — don't materialize full nested arrays

**Today:**
When the object-root streaming path matches the `items` property, it parses the ENTIRE array into a `JsonArray`, then walks it with `JsonPathMatcher.FindMatches`.

**Problem:** `Stream_All_Wildcard` on `$.items[*].id`:
1. Parses full 1000-element array → 1,072,397 B allocation
2. Walks all 1000 items to extract `id`
3. Returns all 1000 matches
4. Gen2 GC triggers

**Fix:** For `*.items[0].name` (single index), stop after the parse and use direct array access instead of full tree walk. For `*.items[*].id` with subsequent property access, parse the array lazily — deserialize one element at a time from the streaming path, extract the `id` property, yield it, and discard.

**Implementation:**
```csharp
// After finding the 'items' property value, instead of:
var propertyNode = JsonNode.Parse(rawText); // Materializes ALL 1000 items

// Do:
using var doc = JsonDocument.Parse(rawText);
var itemsArray = doc.RootElement;
foreach (var item in itemsArray.EnumerateArray())
{
    if (item.TryGetProperty("id", out var idElement))
        yield return JsonNode.Parse(idElement.GetRawText()); // Only parse 'id' values
}
```

For `ExtractFirst` with index, don't enumerate all — jump to the index:
```csharp
var item = itemsArray[500]; // Direct access via JsonElement indexer
if (item.TryGetProperty("name", out var nameElement))
    return JsonNode.Parse(nameElement.GetRawText());
```

**Expected impact:**
- `Stream_First_ArrayIndex`: 809.7 μs → **~50 μs** (16× faster), 478,881 B → **~5,000 B** (96× less)
- `Stream_All_Wildcard`: 1,640 μs → **~200 μs** (8× faster), 1,072,397 B → **~100,000 B** (10× less)
- **Eliminates Gen2 GC** on wildcard benchmarks

---

### 🟡 Priority 4: Eliminate `segments.Skip(1).ToList()` in streaming methods

**Today:** Every streaming method does:
```csharp
var remainingSegments = segments.Skip(1).ToList(); // Allocates a new list
```

**Fix:** Pass segments as `ReadOnlySpan<JsonPathSegment>` or use a `startIndex` parameter.

```csharp
// Instead of:
var remainingSegments = segments.Skip(1).ToList();
JsonPathExtractionCore.FindFirstMatch(item, remainingSegments);

// Use:
JsonPathExtractionCore.FindFirstMatch(item, segments, startIndex: 1);
```

**Expected impact:** Eliminates ~6 list allocations per streaming call (one for each of the 6 streaming methods). Small but cumulative win.

---

### 🟡 Priority 5: Pool intermediate lists in `JsonPathMatcher`

**Today:** `FindSegmentMatches` creates `new List<JsonNode?>()` for every segment transition.

```csharp
private static List<JsonNode?> FindSegmentMatches(IEnumerable<JsonNode?> current, JsonPathSegment segment)
{
    var results = new List<JsonNode?>(); // New list every time
    foreach (var node in current) { ... }
    return results;
}
```

**Fix:** Pre-allocate with capacity hints and consider pooling.

```csharp
var results = new List<JsonNode?>(capacity: current is ICollection<JsonNode?> c ? c.Count : 4);
```

Or use `ArrayPool`-backed lists for hot paths.

**Expected impact:** 10–15% allocation reduction in the matching layer.

---

### 🟡 Priority 6: Use `JsonElement` in root-array streaming path

**Today:** `DeserializeAsyncEnumerable<JsonNode>` deserializes each array element into a full `JsonNode` tree.

**Fix:** Use `JsonSerializer.DeserializeAsyncEnumerable` with a custom approach, or parse the raw byte range for each element using `JsonDocument.Parse`.

Actually, `DeserializeAsyncEnumerable<JsonElement>` doesn't exist in the same way. Alternative: Use `Utf8JsonReader` to find element boundaries, then `JsonDocument.Parse` each element as needed.

For index-based access (e.g., `$[250].name`), skip to the 250th element without parsing the first 249.

**Expected impact:**
- `Stream_RootArray_First_Index`: 304.7 μs → **~100 μs** (3× faster)
- `Stream_RootArray_All_Wildcard`: 821.3 μs → **~400 μs** (2× faster), 587,985 B → **~200,000 B**

---

### 🟢 Priority 7: Reuse peek result — avoid double `GetRootContainerKind`

**Today:** `CanUseStreaming` calls `GetRootContainerKind(stream)`, then `ExtractFirstMatchAsync` calls it again.

**Fix:** Return the container kind from `CanUseStreaming` via an `out` parameter, or cache it.

**Expected impact:** Eliminates one stream seek + StreamReader creation per call. ~1–5 μs savings.

---

### 🟢 Priority 8: Replace `StreamReader` peek with byte-level peek

**Today:** `TryPeekRootToken` creates a `StreamReader` to find the first non-whitespace character.

**Fix:** Read a small buffer (e.g., 16 bytes) and scan for `[` or `{` without creating a `StreamReader`.

```csharp
Span<byte> buffer = stackalloc byte[16];
int read = stream.Read(buffer);
stream.Position = origin;
for (int i = 0; i < read; i++) {
    if (buffer[i] == '[' || buffer[i] == '{') return (char)buffer[i];
    if (!char.IsWhiteSpace((char)buffer[i])) break; // Not JSON
}
```

**Expected impact:** ~2–5 μs savings, zero allocation for peek.

---

## 3. Expected Cumulative Impact

| Benchmark | Current Mean | Target Mean | Current Alloc | Target Alloc |
|---|---|---|---|---|
| `Stream_First_SimpleProperty` | 73.7 μs | **~15 μs** (5×) | 54,616 B | **~5,000 B** (11×) |
| `Stream_First_ArrayIndex` | 809.7 μs | **~50 μs** (16×) | 478,881 B | **~5,000 B** (96×) |
| `Stream_First_Wildcard` | 1,267 μs | **~200 μs** (6×) | 1,055,273 B | **~50,000 B** (21×) |
| `Stream_All_Wildcard` | 1,640 μs | **~200 μs** (8×) | 1,072,397 B | **~100,000 B** (11×) |
| `Stream_All_Range` | 866 μs | **~100 μs** (9×) | 485,105 B | **~30,000 B** (16×) |
| `Stream_RootArray_First_Index` | 304.7 μs | **~100 μs** (3×) | 136,952 B | **~40,000 B** (3×) |
| `Stream_RootArray_All_Wildcard` | 821.3 μs | **~400 μs** (2×) | 587,985 B | **~200,000 B** (3×) |

**Gen2 GC on large arrays: Eliminated.**

---

## 4. Implementation Order

| Step | Effort | Risk | Impact |
|---|---|---|---|
| 1. Lazy array matching (Priority 3) | Medium | Medium | 🏆 Highest |
| 2. Eliminate double parse (Priority 2) | Medium-High | Medium | 🏆 Highest |
| 3. Remove `ReadToEndAsync` (Priority 1) | Low-Medium | Low | High |
| 4. Remove `Skip(1).ToList()` (Priority 4) | Low | Low | Medium |
| 5. Pool intermediate lists (Priority 5) | Low | Low | Medium |
| 6. JsonElement in root-array path (Priority 6) | Medium | Medium | Medium-High |
| 7. Reuse peek result (Priority 7) | Low | Low | Low |
| 8. Byte-level peek (Priority 8) | Low | Low | Low |

**Recommended first sprint:** Steps 1 + 2 + 3 → target **5–16× speedup** on common Stream paths.

---

*This plan should be reviewed and updated after each implementation step based on re-benchmarking.*
