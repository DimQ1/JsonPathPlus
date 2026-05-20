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

    /// <summary>Store-shaped object for nested query benchmarks.</summary>
    public static readonly string Store = GenerateStore();

    private static string GenerateStore()
    {
        var books = new System.Text.StringBuilder();
        books.Append('[');
        for (int i = 0; i < 50; i++)
        {
            if (i > 0) books.Append(',');
            books.Append($"{{\"title\":\"Book {i}\",\"author\":\"Author {i}\",\"price\":{5.0 + i * 2},\"isbn\":\"isbn-{i}\",\"category\":{(i % 3 == 0 ? "\"fiction\"" : i % 3 == 1 ? "\"non-fiction\"" : "\"reference\"")}}}");
        }
        books.Append(']');

        return $$"""
        {
            "store": {
                "name": "Benchmark Bookstore",
                "location": "Test City",
                "book": {{books}},
                "bicycle": { "color": "red", "price": 300, "brand": "TestBrand" },
                "magazines": [
                    { "title": "Mag A", "issue": 1, "price": 5 },
                    { "title": "Mag B", "issue": 2, "price": 7 }
                ]
            }
        }
        """;
    }

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
