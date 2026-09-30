using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Allocation-focused benchmarks for the filter and computed-index evaluators.
/// Uses pre-parsed <see cref="JsonNode"/> input so the measured allocations
/// isolate the matching/evaluation hot path from JSON deserialization.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class JsonPathFilterAllocationBenchmarks
{
    private JsonNode? _largeNode;

    [GlobalSetup]
    public void GlobalSetup()
        => _largeNode = JsonNode.Parse(BenchmarkData.Large);

    // ── Filter ────────────────────────────────────────────────────────

    [Benchmark]
    public JsonNode? Filter_First()
        => _largeNode!.ExtractFirstJsonMatchAsync("$.items[?(@.score >= 750)].name").GetAwaiter().GetResult();

    [Benchmark]
    public int Filter_All()
    {
        int count = 0;
        var enumerator = _largeNode!.ExtractAllJsonMatchesAsync("$.items[?(@.score >= 750)].name").GetAsyncEnumerator();
        while (enumerator.MoveNextAsync().GetAwaiter().GetResult())
            count++;

        return count;
    }

    // ── Computed index ────────────────────────────────────────────────

    [Benchmark]
    public JsonNode? ComputedIndex_First()
        => _largeNode!.ExtractFirstJsonMatchAsync("$.items[(@.length-1)].name").GetAwaiter().GetResult();

    [Benchmark]
    public int ComputedIndex_All()
    {
        int count = 0;
        var enumerator = _largeNode!.ExtractAllJsonMatchesAsync("$.items[(@.length-1)].name").GetAsyncEnumerator();
        while (enumerator.MoveNextAsync().GetAwaiter().GetResult())
            count++;

        return count;
    }
}
