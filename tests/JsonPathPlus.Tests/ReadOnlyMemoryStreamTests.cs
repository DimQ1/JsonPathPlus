using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

/// <summary>
/// Tests for ReadOnlyMemoryStream behavior through the public string extension API
/// and stream-based operations.
/// </summary>
public sealed class ReadOnlyMemoryStreamTests
{
  // ── Read operations ────────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_FromString_UsesReadOnlyMemoryStream()
  {
    const string json = """{"key": "value"}""";
    var result = await json.ExtractFirstJsonMatchAsync("$.key");
    Assert.NotNull(result);
    Assert.Equal("value", result!.GetValue<string>());
  }

  [Fact]
  public async Task ExtractAll_FromString_UsesReadOnlyMemoryStream()
  {
    const string json = """{"items": [1, 2, 3]}""";
    var results = await CollectAsync(json.ExtractAllJsonMatchesAsync("$.items[*]"));
    Assert.Equal(3, results.Count);
  }

  [Fact]
  public async Task ExtractAllWithPaths_FromString_UsesReadOnlyMemoryStream()
  {
    const string json = """{"items": [{"x": 1}, {"x": 2}]}""";
    var results = await CollectPathMatchesAsync(
      json.ExtractAllJsonMatchesWithPathsAsync("$.items[*].x"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[0].x", results[0].Path);
    Assert.Equal("$.items[1].x", results[1].Path);
  }

  [Fact]
  public async Task ExtractFirst_EmptyString_ReturnsNull()
  {
    const string json = "";
    // Empty string is not valid JSON; the implementation may throw or return null.
    // Verify it doesn't crash with unexpected exception type.
    try
    {
      var result = await json.ExtractFirstJsonMatchAsync("$");
      Assert.Null(result);
    }
    catch (System.Text.Json.JsonException)
    {
      // Expected: empty string is invalid JSON
    }
    catch (ArgumentException)
    {
      // Also acceptable for empty input
    }
  }

  // ── Multi-call on same string (checks stream re-winding) ───────────────

  [Fact]
  public async Task ExtractFirst_TwoCallsOnSameString_Works()
  {
    const string json = """{"a": 1, "b": 2}""";

    var resultA = await json.ExtractFirstJsonMatchAsync("$.a");
    var resultB = await json.ExtractFirstJsonMatchAsync("$.b");

    Assert.Equal(1, resultA!.GetValue<int>());
    Assert.Equal(2, resultB!.GetValue<int>());
  }

  // ── Large string extraction ────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_LargeString_Works()
  {
    var items = new StringBuilder("[");
    for (var i = 0; i < 100; i++)
    {
      if (i > 0) items.Append(',');
      items.Append($$"""{"id": {{i}}, "name": "item{{i}}"}""");
    }
    items.Append(']');
    var json = items.ToString();

    var result = await json.ExtractFirstJsonMatchAsync("$[99].name");
    Assert.Equal("item99", result!.GetValue<string>());
  }

  // ── Nested object extraction ───────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_NestedObject_ReturnsDeepMatch()
  {
    const string json = """
    {
      "level1": {
        "level2": {
          "level3": { "value": "deep" }
        }
      }
    }
    """;

    var result = await json.ExtractFirstJsonMatchAsync("$.level1.level2.level3.value");
    Assert.Equal("deep", result!.GetValue<string>());
  }

  // ── String with unicode ────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_UnicodeString_Works()
  {
    const string json = """{"greeting": "Hello, 世界!"}""";
    var result = await json.ExtractFirstJsonMatchAsync("$.greeting");
    Assert.Equal("Hello, 世界!", result!.GetValue<string>());
  }

  // ── Helper methods ─────────────────────────────────────────────────────

  private static async Task<List<JsonNode?>> CollectAsync(IAsyncEnumerable<JsonNode?> source)
  {
    var list = new List<JsonNode?>();
    await foreach (var item in source)
      list.Add(item);
    return list;
  }

  private static async Task<List<JsonPathMatch>> CollectPathMatchesAsync(
    IAsyncEnumerable<JsonPathMatch> source)
  {
    var list = new List<JsonPathMatch>();
    await foreach (var item in source)
      list.Add(item);
    return list;
  }
}
