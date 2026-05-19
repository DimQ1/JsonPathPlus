using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="JsonPathValidator.IsValid(string?)"/> and
/// <see cref="JsonPathValidator.Validate(string?)"/>.
/// </summary>
[MemoryDiagnoser]
public class JsonPathValidatorBenchmarks
{
    [Benchmark]
    public bool IsValid_Simple()
        => JsonPathValidator.IsValid("$.items[0].name");

    [Benchmark]
    public bool IsValid_Complex_Filter()
        => JsonPathValidator.IsValid("$.store.book[?(@.price < 10 && @.category == 'fiction')].title");

    [Benchmark]
    public bool IsValid_RecursiveDescent()
        => JsonPathValidator.IsValid("$..target");

    [Benchmark]
    public bool IsValid_Range()
        => JsonPathValidator.IsValid("$.items[0:10].name");

    [Benchmark]
    public JsonPathValidationResult Validate_Simple()
        => JsonPathValidator.Validate("$.items[0].name");

    [Benchmark]
    public JsonPathValidationResult Validate_Invalid()
        => JsonPathValidator.Validate("$.items[[bad].name");
}
