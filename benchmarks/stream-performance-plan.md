# Library Evolution Plan: Memory, Performance & Large-File Streaming

**Date:** 2026-05-19  
**Primary goal:** Make JsonPathPlus safe and efficient for JSON payloads from **1 KB to 1 GB**, with a focus on minimizing peak memory and avoiding Out-Of-Memory conditions.

---

## Library Core Requirements (Non-Negotiable)

1. **Payload range: 1 KB → 1 GB.** The library must not OOM on a 1 GB JSON stream when the query is structurally streamable (simple first-segment match over root array/object). Graceful degradation (exception with clear message) is acceptable for queries requiring full-document materialization beyond a configurable budget.
2. **Streaming must not buffer the entire document.** The `ReadToEndAsync` pattern that copies the full stream into a `byte[]` must be eliminated from all streaming paths. Streaming code must operate over a bounded window of the input.
3. **Non-seekable streams must be supported.** `CanUseStreaming` currently requires `stream.CanSeek`. The library must detect root container kind and apply streaming without requiring seekability (e.g., from network streams, gzip wrappers, pipes).
4. **`FullParseMaxBytes` must guard ALL materialization paths**, not just the full-parse fallback. If a query would materialize more than the budget, it must fail with a clear diagnostic.
5. **Return type must support non-materialized results.** Today all APIs return materialized `JsonNode` / `JsonPathMatch`. For large subtrees, an option to return `JsonElement` (backed by a pooled buffer) or a streaming result must exist so callers can avoid full subtree materialization.
6. **Existing API surface preserved.** All current `StreamJsonExtractionExtensions` signatures remain functional. New capability added via new overloads or options flags, not breaking changes.

---

## Scope and principles

1. Optimize memory first; treat latency gains as secondary benefits.
2. Preserve all public APIs in `StreamJsonExtractionExtensions`.
3. Keep streaming fallback behavior correct for unsupported segment types.
4. Require benchmark evidence for each change before broad rollout.
5. All streaming paths must work with a bounded memory window, not a full-stream copy.

---

## Current memory hotspots (code-based)

1. **Full-stream buffering (blocker for 1 GB).** All six streaming methods in `JsonPathStreamingMatcher` call `ReadToEndAsync`, which allocates `new byte[(int)remainingLength]` — a 1 GB contiguous allocation for a 1 GB stream. This is the #1 OOM risk.
2. **`CanUseStreaming` requires `stream.CanSeek`.** Non-seekable streams always fall through to full parse, which materializes the entire document as `JsonNode`.
3. **`FullParseMaxBytes` only guards the full-parse fallback.** The streaming path itself has no memory budget enforcement.
4. **Return type forces materialization.** All public APIs return `JsonNode` / `JsonPathMatch`, which must be a fully materialized tree. For a 500 MB subtree matched by a query, this is unavoidable OOM.
5. Object-root fallback still materializes `JsonNode` via `JsonNode.Parse(propertyDocument.RootElement.GetRawText())` in `JsonPathStreamingMatcher`.
6. Matcher traversal allocates new lists on each segment step (`FindSegmentMatches`, `FindSegmentMatchesWithPaths`) in `JsonPathMatcher`.
7. Filter evaluation allocates for expression parsing (`Trim`, recursive substring slices, `Split('.')`) in `JsonPathFilterEvaluator`.
8. Parser allocates intermediate strings/arrays for unions and projections (`ToString().Split(...)`) in `JsonPathParser`.
9. Root container detection is repeated (`CanUseStreaming` plus extract method path selection) in `JsonPathStreamingMatcher`, causing extra stream peeks.

---

## Large-File Streaming Architecture (New)

This section describes the architectural shift needed to satisfy the core requirements for 1 GB+ payloads, independent of the smaller memory optimization phases below.

### The root problem

Every streaming path today does:

```
Stream → ReadToEndAsync → byte[] (ENTIRE document in memory) → Utf8JsonReader scan
```

This is not "streaming" — it's "buffered-then-scanned." For a 1 GB file, this requires 1 GB of contiguous memory before any matching begins.

### Required architecture

```
Stream → Bounded Buffer Window (e.g., 64 KB pooled) → Utf8JsonReader over buffer → match/yield/return
```

Key design decisions:

| Decision | Approach |
|---|---|
| **Buffer management** | `ArrayPool<byte>` backed; refill window when reader advances past buffer boundary; never hold the full stream in memory |
| **Root container detection** | Read first few bytes from stream (no `StreamReader`, no seek required); peek the first non-whitespace UTF-8 byte for `[` or `{` |
| **Seekless streaming** | `CanUseStreaming` drops the `CanSeek` requirement; root kind detected from initial read; position tracking is local to the buffered window |
| **Element skipping** | `Utf8JsonReader.Skip()` already handles this for unmatched elements — no materialization needed |
| **Result return** | New public API surface: `ExtractFirstJsonElementAsync` / `ExtractAllJsonElementsAsync` returning `JsonElement` backed by the caller-owned buffer window. Existing `JsonNode`-returning APIs continue to work for small payloads |
| **Memory budget** | `FullParseMaxBytes` renamed/re-scoped to `MaxMaterializationBytes`; enforced at every point where a subtree would be materialized, including matched elements in the streaming path |
| **Fallback to full parse** | Only allowed when `stream.Length <= MaxMaterializationBytes`; otherwise throws with a clear message recommending a streamable selector |

### Return-type strategy

| API family | Returns | Use case |
|---|---|---|
| `ExtractFirstJsonMatchAsync` (existing) | `JsonNode?` | Small payloads, backward compat |
| `ExtractAllJsonMatchesAsync` (existing) | `IAsyncEnumerable<JsonNode?>` | Small payloads, backward compat |
| **New:** `ExtractFirstJsonElementAsync` | `JsonElement?` with `IDisposable` owner | Large payloads, caller disposes buffer |
| **New:** `ExtractAllJsonElementsAsync` | `IAsyncEnumerable<JsonElement>` with pooled lifetime | Large payloads, caller iterates within buffer window |
| **New:** `CopyFirstJsonMatchAsync` | `Task` + `Stream` output | Large payloads, writes matched subtree directly to output stream |

### Migration path (no breaking changes)

1. Add `JsonElement`-returning overloads as new public methods.
2. Make existing `JsonNode`-returning overloads delegate to `JsonElement` overloads + `JsonNode.Parse` for small payloads.
3. Add `MaxMaterializationBytes` option that gates all materialization.
4. Deprecate `FullParseMaxBytes` in favor of the new option (keep it as an alias).

---

## Memory-first roadmap (small-to-medium payloads)

---

## Baseline metrics to beat

Use `benchmarks/benchmark-results-v1.md` and latest BenchmarkDotNet report snapshots as baseline for allocation deltas.

Priority memory baselines:

1. `Stream_All_Wildcard`: ~1.07 MB allocated/op.
2. `Stream_First_Wildcard`: ~1.06 MB allocated/op.
3. `Stream_All_Range`: ~485 KB allocated/op.
4. `Stream_First_ArrayIndex`: ~479 KB allocated/op.

Success criteria for memory track:

1. Cut allocations by at least 40% on the four scenarios above.
2. Remove Gen2 collections for wildcard/range stream benchmarks.
3. Keep correctness parity with existing tests.

---

## Memory-first roadmap

### Phase 1: Remove avoidable buffering and reparsing

#### 1.1 Replace unconditional `ReadToEndAsync` with pooled buffering

Target files:

1. `JsonPathStreamingMatcher.cs`

Actions:

1. Replace `ReadToEndAsync` usages with an `ArrayPool<byte>`-backed read helper.
2. Keep `Utf8JsonReader`-based flow, but return pooled buffer in `finally`.
3. Avoid creating extra `byte[]` copies when stream length is known.

Expected memory impact:

1. Lower temporary buffer churn on hot stream paths.
2. Smaller LOH pressure on larger payloads.

#### 1.2 Eliminate object-root fallback raw-text roundtrip when possible

Target files:

1. `JsonPathStreamingMatcher.cs`
2. `JsonPathExtractionCore.cs` (new internal entry points if needed)

Actions:

1. Expand `JsonElement` matcher support so more segment types stay in element pipeline.
2. Reduce reliance on fallback that calls `GetRawText` + `JsonNode.Parse`.
3. Keep fallback only for truly unsupported segment families.

Expected memory impact:

1. Lower string allocations and parser allocations on object-root stream selectors.

---

### Phase 2: Reduce traversal allocations in matching

#### 2.1 Rework segment traversal to reuse buffers

Target files:

1. `JsonPathMatcher.cs`

Actions:

1. Replace per-segment new lists with two reusable buffers (`current`, `next`) and swap/clear.
2. Add capacity hints from current set size.
3. Mirror this strategy for path-aware traversal (`MatchContext`).

Expected memory impact:

1. Fewer short-lived list allocations per query.
2. Reduced Gen0 churn for deep or wildcard-heavy selectors.

#### 2.2 Add first-match short-circuit traversal path ✅ **DONE**

Target files:

1. `JsonPathExtractionCore.cs`
2. `JsonPathMatcher.cs`

Actions:

1. ✅ Introduced `TryFindFirstMatch` recursive traversal that exits as soon as one match is found.
2. ✅ Routed `FindFirstMatch` in `JsonPathExtractionCore` through the new short-circuit path.
3. ✅ Avoids building full match collections for `ExtractFirst...` APIs.
4. ✅ Full segment-type coverage: Property, ArrayIndex, ArrayRange, ArrayUnion, PropertyUnion, Filter, ComputedIndex, Wildcard, RecursiveDescent, FieldProjection, FieldExclusion, FieldExistence, FieldCount.

Validation: 240/240 `StreamJsonExtractionExtensionsTests` pass across net8.0, net9.0, net10.0.

---

### Phase 3: Cut parser and filter allocation overhead

#### 3.1 Span-first parsing for unions/projections

Target files:

1. `JsonPathParser.cs`

Actions:

1. Replace `ToString().Split(...)` patterns with span tokenization loops.
2. Allocate arrays only once final token count is known.
3. Keep validation rules unchanged.

Expected memory impact:

1. Lower path-parse allocations for complex selectors.

#### 3.2 Filter evaluator expression cache and non-splitting path resolution

Target files:

1. `JsonPathFilterEvaluator.cs`

Actions:

1. Cache parsed filter AST/token plan for repeated expressions.
2. Replace `path.Split('.')` with span scanning.
3. Keep comparison semantics unchanged.

Expected memory impact:

1. Significant improvement for repeated filter expressions across array items.

---

### Phase 4: Streaming decision and control-plane cleanup

Target files:

1. `JsonPathStreamingMatcher.cs`
2. `StreamJsonExtractionExtensions.cs`

Actions:

1. Avoid duplicate root-kind probing by returning root kind from streaming eligibility check.
2. Consolidate stream position restore logic in one place.

Expected memory impact:

1. Minor direct allocation gains, cleaner hot-path control flow.

---

## Benchmark and validation plan

1. Keep `[MemoryDiagnoser]` benchmark suite in `tests/JsonPathPlus.Benchmarks/JsonPathExtractionBenchmarks.cs` as primary guardrail.
2. Add dedicated memory cases for:
3. `ExtractAllJsonMatchesWithPathsAsync` (object root and root array).
4. Filters over root arrays (to validate Phase 3 wins).
5. Run targeted tests first:
6. `tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj --filter FullyQualifiedName~StreamJsonExtractionExtensionsTests`
7. Re-run full benchmark comparison after each phase.

---

## Proposed implementation order (memory impact vs risk)

### Completed
1. ✅ Phase 2.2 first-match short-circuit (high impact, moderate risk)

### Next — small-to-medium payload optimizations
2. Phase 2.1 reusable matcher buffers (high impact, low-moderate risk)
3. Phase 1.2 expand JsonElement support to reduce fallback (high impact, moderate risk)
4. Phase 1.1 pooled buffering replacement (medium impact, low risk)
5. Phase 3.2 filter cache and span path resolution (medium impact, moderate risk)
6. Phase 3.1 parser span tokenization (medium impact, low-moderate risk)
7. Phase 4 streaming decision cleanup (small impact, low risk)

### Large-file streaming phases (dependency-ordered)
8. ✅ **LFS-1: Seekless root detection.** Replaced `TryPeekRootToken` / `StreamReader` with raw byte peek via `PeekRootKindFromBytes`; removed `CanSeek` requirement from `CanUseStreaming`; introduced `StreamHead` type to flow peek results to extract methods. For non-seekable streams, head bytes are captured and prepended.  
   **Validation:** 450/450 tests pass across net8.0/net9.0/net10.0; evaluator compiles.
9. **LFS-2: Bounded-window `Utf8JsonReader`.** Replace `ReadToEndAsync` with a chunked reader that fills a pooled buffer, advances `Utf8JsonReader` over buffer windows, and refills when needed. This is the core architectural change.
10. **LFS-3: `MaxMaterializationBytes` enforcement.** Gate every subtree materialization (matched elements, fallback paths) on the budget; throw with clear diagnostics when exceeded.
11. **LFS-4: `JsonElement`-returning public API.** New `ExtractFirstJsonElementAsync` / `ExtractAllJsonElementsAsync` overloads; existing APIs delegate to these + `JsonNode.Parse` for small payloads.
12. **LFS-5: `CopyFirstJsonMatchAsync`.** Stream-to-stream extraction for the largest subtrees — write matched subtree directly to caller's output stream without intermediate materialization.
13. **LFS-6: 1 GB integration tests.** Add stress tests with generated 1 GB root-array/object payloads; verify bounded memory and correct results.

---

## Non-goals for this plan

1. Public API signature changes.
2. Semantics changes in JSONPath behavior.
3. Premature use of complex custom allocators unless benchmark evidence justifies it.

---

## Exit criteria

### Small-to-medium payload track
1. Allocation reduction target reached for stream-heavy wildcard/range/index scenarios.
2. No regression in `StreamJsonExtractionExtensionsTests`.
3. Updated benchmark report committed with before/after allocation table.

### Large-file streaming track
1. `ReadToEndAsync` eliminated from all streaming paths.
2. `CanUseStreaming` works without `stream.CanSeek`.
3. 1 GB root-array query (`$[0].name`) completes with bounded memory (peak < 10 MB regardless of file size).
4. 1 GB root-object query (`$.singleKey.subkey`) completes with bounded memory.
5. `MaxMaterializationBytes` blocks materialization attempts beyond budget.
6. New `JsonElement`-returning APIs exist and are tested.

---

This plan is intentionally memory-centric. If benchmark data shows a tradeoff between lower allocation and minor throughput variance, prefer lower allocation unless latency regression exceeds 10% on common paths.
