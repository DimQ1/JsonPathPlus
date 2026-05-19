using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Benchmarks for JSONPath expression parsing (tokenization) only — no actual matching.
/// </summary>
[MemoryDiagnoser]
public class JsonPathParsingBenchmarks
{
    [Benchmark]
    [Arguments("$.items[0].name")]
    [Arguments("$.a.b.c.d.e.f.g.h")]
    [Arguments("$.items[*].id")]
    [Arguments("$.store.book[?(@.price < 10)].title")]
    [Arguments("$..target")]
    [Arguments("$.items[0:10].name")]
    [Arguments("$.items[?(@.active == true)].id")]
    [Arguments("$.deep.nested[?(@.count > 5 && @.score <= 100)].label")]
    public bool Parse(string path) => JsonPathValidator.IsValid(path);
}
