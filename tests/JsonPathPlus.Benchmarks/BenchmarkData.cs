namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Shared JSON documents used across benchmarks.
/// </summary>
internal static class BenchmarkData
{
    /// <summary>Flat object with ~10 properties.</summary>
    public const string Small = """
        {
            "id": 1,
            "name": "Example",
            "active": true,
            "score": 3.14,
            "tags": ["a", "b", "c"],
            "meta": { "version": 1, "author": "demo" },
            "counts": [10, 20, 30],
            "flag": false,
            "label": "test",
            "nested": { "deep": { "value": 42 } }
        }
        """;

    /// <summary>Object with a 100-element array.</summary>
    public static readonly string Medium = GenerateMedium();

    /// <summary>Object with a 1000-element array.</summary>
    public static readonly string Large = GenerateLarge();

    /// <summary>Deeply nested object for recursive descent tests.</summary>
    public const string DeeplyNested = """
        {
            "a": {
                "b": {
                    "c": {
                        "d": {
                            "e": {
                                "target": "found",
                                "value": 42
                            }
                        }
                    }
                }
            },
            "other": { "target": "shallow" }
        }
        """;

    /// <summary>Root-level array for streaming benchmarks.</summary>
    public static readonly string RootArray = GenerateRootArray(500);

    private static string GenerateMedium()
    {
        var items = new System.Text.StringBuilder();
        items.Append("{\"items\":[");
        for (int i = 0; i < 100; i++)
        {
            if (i > 0) items.Append(',');
            items.Append($"{{\"id\":{i},\"name\":\"item{i}\",\"active\":{(i % 2 == 0).ToString().ToLowerInvariant()},\"score\":{i * 1.5}}}");
        }
        items.Append("]}");
        return items.ToString();
    }

    private static string GenerateLarge()
    {
        var items = new System.Text.StringBuilder();
        items.Append("{\"items\":[");
        for (int i = 0; i < 1000; i++)
        {
            if (i > 0) items.Append(',');
            items.Append($"{{\"id\":{i},\"name\":\"item{i}\",\"active\":{(i % 2 == 0).ToString().ToLowerInvariant()},\"score\":{i * 1.5}}}");
        }
        items.Append("]}");
        return items.ToString();
    }

    private static string GenerateRootArray(int count)
    {
        var items = new System.Text.StringBuilder();
        items.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) items.Append(',');
            items.Append($"{{\"id\":{i},\"name\":\"item{i}\",\"nested\":{{\"value\":{i * 10}}}}}");
        }
        items.Append(']');
        return items.ToString();
    }
}
