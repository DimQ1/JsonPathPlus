using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Benchmarks covering every public API overload listed in the README:
/// Stream, Stream+Options, JsonNode, and string inputs for both First and All.
/// </summary>
[MemoryDiagnoser]
public class JsonPathExtractionBenchmarks
{
    private JsonNode? _mediumNode;
    private JsonNode? _largeNode;
    private JsonNode? _deeplyNestedNode;
    private JsonNode? _rootArrayNode;
    private Stream _mediumStream = null!;
    private Stream _largeStream = null!;
    private Stream _deeplyNestedStream = null!;
    private Stream _rootArrayStream = null!;
    private JsonPathExtractionOptions _defaultOptions;
    private JsonPathExtractionOptions _cappedOptions;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _mediumNode = JsonNode.Parse(BenchmarkData.Medium);
        _largeNode = JsonNode.Parse(BenchmarkData.Large);
        _deeplyNestedNode = JsonNode.Parse(BenchmarkData.DeeplyNested);
        _rootArrayNode = JsonNode.Parse(BenchmarkData.RootArray);

        _mediumStream = CreateStream(BenchmarkData.Medium);
        _largeStream = CreateStream(BenchmarkData.Large);
        _deeplyNestedStream = CreateStream(BenchmarkData.DeeplyNested);
        _rootArrayStream = CreateStream(BenchmarkData.RootArray);

        _defaultOptions = default;
        _cappedOptions = new JsonPathExtractionOptions { FullParseMaxBytes = 10_000_000 };
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _mediumStream.Dispose();
        _largeStream.Dispose();
        _deeplyNestedStream.Dispose();
        _rootArrayStream.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Stream input
    // ══════════════════════════════════════════════════════════════════

    // ── ExtractFirstJsonMatchAsync(this Stream, string?) ──

    [Benchmark]
    public Task<JsonNode?> Stream_First_SimpleProperty()
        => _mediumStream.ExtractFirstJsonMatchAsync("$.items[0].name");

    [Benchmark]
    public Task<JsonNode?> Stream_First_ArrayIndex()
        => _largeStream.ExtractFirstJsonMatchAsync("$.items[500].name");

    [Benchmark]
    public Task<JsonNode?> Stream_First_Wildcard()
        => _largeStream.ExtractFirstJsonMatchAsync("$.items[*].name");

    [Benchmark]
    public Task<JsonNode?> Stream_First_RecursiveDescent()
        => _deeplyNestedStream.ExtractFirstJsonMatchAsync("$.a..target");

    // ── ExtractAllJsonMatchesAsync(this Stream, string?) ──

    [Benchmark]
    public Task Stream_All_Wildcard()
        => ConsumeAll(_largeStream.ExtractAllJsonMatchesAsync("$.items[*].id"));

    [Benchmark]
    public Task Stream_All_Range()
        => ConsumeAll(_largeStream.ExtractAllJsonMatchesAsync("$.items[0:10].name"));

    // ══════════════════════════════════════════════════════════════════
    //  Stream input with JsonPathExtractionOptions
    // ══════════════════════════════════════════════════════════════════

    // ── ExtractFirstJsonMatchAsync(this Stream, string?, options) ──

    [Benchmark]
    public Task<JsonNode?> StreamOptions_First()
        => _mediumStream.ExtractFirstJsonMatchAsync("$.items[0].name", _defaultOptions);

    // ── ExtractAllJsonMatchesAsync(this Stream, string?, options) ──

    [Benchmark]
    public Task StreamOptions_All()
        => ConsumeAll(_mediumStream.ExtractAllJsonMatchesAsync("$.items[0:10].name", _defaultOptions));

    // ── With FullParseMaxBytes cap ──

    [Benchmark]
    public Task<JsonNode?> StreamOptionsCapped_First()
        => _mediumStream.ExtractFirstJsonMatchAsync("$.items[0].name", _cappedOptions);

    [Benchmark]
    public Task StreamOptionsCapped_All()
        => ConsumeAll(_mediumStream.ExtractAllJsonMatchesAsync("$.items[0:10].name", _cappedOptions));

    // ══════════════════════════════════════════════════════════════════
    //  JsonNode input
    // ══════════════════════════════════════════════════════════════════

    // ── ExtractFirstJsonMatchAsync(this JsonNode?, string?) ──

    [Benchmark]
    public JsonNode? Node_First_SimpleProperty()
        => _mediumNode!.ExtractFirstJsonMatchAsync("$.items[0].name").GetAwaiter().GetResult();

    [Benchmark]
    public JsonNode? Node_First_NestedProperty()
        => _deeplyNestedNode!.ExtractFirstJsonMatchAsync("$.a.b.c.d.e.value").GetAwaiter().GetResult();

    [Benchmark]
    public JsonNode? Node_First_Root()
        => _largeNode!.ExtractFirstJsonMatchAsync(null).GetAwaiter().GetResult();

    // ── ExtractAllJsonMatchesAsync(this JsonNode?, string?) ──

    [Benchmark]
    public int Node_All_Wildcard()
    {
        int count = 0;
        var e = _largeNode!.ExtractAllJsonMatchesAsync("$.items[*].id").GetAsyncEnumerator();
        while (e.MoveNextAsync().GetAwaiter().GetResult())
            count++;
        return count;
    }

    [Benchmark]
    public int Node_All_Range()
    {
        int count = 0;
        var e = _mediumNode!.ExtractAllJsonMatchesAsync("$.items[0:10].name").GetAsyncEnumerator();
        while (e.MoveNextAsync().GetAwaiter().GetResult())
            count++;
        return count;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Raw JSON string input
    // ══════════════════════════════════════════════════════════════════

    // ── ExtractFirstJsonMatchAsync(this string, string?) ──

    [Benchmark]
    public JsonNode? String_First_SimpleProperty()
        => BenchmarkData.Medium.ExtractFirstJsonMatchAsync("$.items[0].name").GetAwaiter().GetResult();

    [Benchmark]
    public JsonNode? String_First_ArrayIndex()
        => BenchmarkData.Large.ExtractFirstJsonMatchAsync("$.items[500].name").GetAwaiter().GetResult();

    [Benchmark]
    public JsonNode? String_First_RecursiveDescent()
        => BenchmarkData.DeeplyNested.ExtractFirstJsonMatchAsync("$.a..target").GetAwaiter().GetResult();

    [Benchmark]
    public JsonNode? String_First_Root()
        => BenchmarkData.Large.ExtractFirstJsonMatchAsync(null).GetAwaiter().GetResult();

    // ── ExtractAllJsonMatchesAsync(this string, string?) ──

    [Benchmark]
    public int String_All_Wildcard()
    {
        int count = 0;
        var e = BenchmarkData.Large.ExtractAllJsonMatchesAsync("$.items[*].id").GetAsyncEnumerator();
        while (e.MoveNextAsync().GetAwaiter().GetResult())
            count++;
        return count;
    }

    [Benchmark]
    public int String_All_Range()
    {
        int count = 0;
        var e = BenchmarkData.Medium.ExtractAllJsonMatchesAsync("$.items[0:10].name").GetAsyncEnumerator();
        while (e.MoveNextAsync().GetAwaiter().GetResult())
            count++;
        return count;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Streaming optimization — root-level array
    // ══════════════════════════════════════════════════════════════════

    [Benchmark]
    public Task<JsonNode?> Stream_RootArray_First_Index()
        => _rootArrayStream.ExtractFirstJsonMatchAsync("$[250].name");

    [Benchmark]
    public Task Stream_RootArray_All_Wildcard()
        => ConsumeAll(_rootArrayStream.ExtractAllJsonMatchesAsync("$[*].name"));

    // ── Helpers ─────────────────────────────────────────────────────

    private static async Task ConsumeAll(IAsyncEnumerable<JsonNode?> source)
    {
        await foreach (var _ in source) { }
    }

    private static Stream CreateStream(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new MemoryStream(bytes);
    }
}
