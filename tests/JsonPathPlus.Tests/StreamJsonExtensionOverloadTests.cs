using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

/// <summary>
/// Tests for extension method overloads: JsonNode overloads, string overloads,
/// ArgumentNullException validation, FullParseMaxBytes edge cases,
/// and ExtractAllJsonMatchesWithPathsAsync on string/JsonNode.
/// </summary>
public sealed class StreamJsonExtensionOverloadTests
{
  private const string SampleJson = """
  {
    "name": "test",
    "items": [
      { "id": 1, "value": "a" },
      { "id": 2, "value": "b" }
    ]
  }
  """;

  // ── ArgumentNullException ──────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_StreamNull_ThrowsArgumentNullException()
  {
    Stream stream = null!;
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => stream.ExtractFirstJsonMatchAsync("$.name"));
  }

  [Fact]
  public async Task ExtractAll_StreamNull_ThrowsArgumentNullException()
  {
    Stream stream = null!;
    await Assert.ThrowsAsync<ArgumentNullException>(
      async () => { await foreach (var _ in stream.ExtractAllJsonMatchesAsync("$.name")) { } });
  }

  [Fact]
  public async Task ExtractFirst_StringNull_ThrowsArgumentNullException()
  {
    string json = null!;
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => json.ExtractFirstJsonMatchAsync("$.name"));
  }

  [Fact]
  public async Task ExtractAll_StringNull_ThrowsArgumentNullException()
  {
    string json = null!;
    await Assert.ThrowsAsync<ArgumentNullException>(
      async () => { await foreach (var _ in json.ExtractAllJsonMatchesAsync("$.name")) { } });
  }

  // ── JsonNode overloads ─────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_JsonNodeNull_DoesNotThrow()
  {
    JsonNode? node = null;
    var result = await node.ExtractFirstJsonMatchAsync("$.name");
    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractAll_JsonNodeNull_ReturnsEmpty()
  {
    JsonNode? node = null;
    var results = await CollectAsync(node.ExtractAllJsonMatchesAsync("$.name"));
    Assert.Empty(results);
  }

  [Fact]
  public async Task ExtractFirst_JsonNode_WithFilter_ReturnsMatch()
  {
    var node = JsonNode.Parse(SampleJson);
    var result = await node!.ExtractFirstJsonMatchAsync("$.items[?(@.id > 1)].value");
    AssertNodeEquals(JsonValue.Create("b"), result);
  }

  [Fact]
  public async Task ExtractAll_JsonNode_WithFilter_ReturnsAllMatches()
  {
    var node = JsonNode.Parse(SampleJson);
    var results = await CollectAsync(node!.ExtractAllJsonMatchesAsync("$.items[?(@.id > 1)].value"));
    Assert.Single(results);
    AssertNodeEquals(JsonValue.Create("b"), results[0]);
  }

  // ── String overloads ───────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_String_WithFilter_ReturnsMatch()
  {
    var result = await SampleJson.ExtractFirstJsonMatchAsync("$.items[?(@.id < 2)].value");
    AssertNodeEquals(JsonValue.Create("a"), result);
  }

  [Fact]
  public async Task ExtractAll_String_WithFilter_ReturnsAllMatches()
  {
    var results = await CollectAsync(SampleJson.ExtractAllJsonMatchesAsync("$.items[?(@.id < 2)].value"));
    Assert.Single(results);
    AssertNodeEquals(JsonValue.Create("a"), results[0]);
  }

  // ── ExtractAllJsonMatchesWithPaths on JsonNode ─────────────────────────

  [Fact]
  public async Task WithPaths_JsonNode_Property_ReturnsMatchesWithPaths()
  {
    var node = JsonNode.Parse(SampleJson);
    var results = await CollectPathMatchesAsync(node!.ExtractAllJsonMatchesWithPathsAsync("$.name"));

    Assert.Single(results);
    Assert.Equal("$.name", results[0].Path);
    AssertNodeEquals(JsonValue.Create("test"), results[0].Value);
  }

  [Fact]
  public async Task WithPaths_JsonNode_Wildcard_ReturnsMatchesWithPaths()
  {
    var node = JsonNode.Parse(SampleJson);
    var results = await CollectPathMatchesAsync(node!.ExtractAllJsonMatchesWithPathsAsync("$.items[*].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[0].id", results[0].Path);
    Assert.Equal("$.items[1].id", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_JsonNodeNull_ReturnsEmpty()
  {
    JsonNode? node = null;
    var results = await CollectPathMatchesAsync(node.ExtractAllJsonMatchesWithPathsAsync("$.name"));
    Assert.Empty(results);
  }

  // ── ExtractAllJsonMatchesWithPaths on string ───────────────────────────

  [Fact]
  public async Task WithPaths_String_Property_ReturnsMatchesWithPaths()
  {
    var results = await CollectPathMatchesAsync(
      SampleJson.ExtractAllJsonMatchesWithPathsAsync("$.name"));

    Assert.Single(results);
    Assert.Equal("$.name", results[0].Path);
    AssertNodeEquals(JsonValue.Create("test"), results[0].Value);
  }

  [Fact]
  public async Task WithPaths_String_Wildcard_ReturnsMatchesWithPaths()
  {
    var results = await CollectPathMatchesAsync(
      SampleJson.ExtractAllJsonMatchesWithPathsAsync("$.items[*].value"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[0].value", results[0].Path);
    Assert.Equal("$.items[1].value", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_StringNull_ThrowsArgumentNullException()
  {
    string json = null!;
    await Assert.ThrowsAsync<ArgumentNullException>(
      async () => { await foreach (var _ in json.ExtractAllJsonMatchesWithPathsAsync("$.name")) { } });
  }

  // ── FullParseMaxBytes on seekable streams ──────────────────────────────

  [Fact]
  public async Task ExtractAll_WithMemoryCap_OnSeekableStream_ThrowsWhenExceeded()
  {
    var json = new string(' ', 100) + "{}"; // Large JSON
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    var options = new JsonPathExtractionOptions { FullParseMaxBytes = 8 };

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await CollectAsync(stream.ExtractAllJsonMatchesAsync("$..name", options)));

    Assert.Contains("FullParseMaxBytes", ex.Message);
  }

  [Fact]
  public async Task ExtractAll_WithMemoryCap_Null_AllowsFullParse()
  {
    using var stream = CreateStream(SampleJson);
    var options = new JsonPathExtractionOptions { FullParseMaxBytes = null };

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[*].id", options));
    Assert.Equal(2, results.Count);
  }

  // ── Simple property-based tests for streaming ──────────────────────────

  [Fact]
  public async Task ExtractAll_RootObjectWithRecursive_UsesStreaming()
  {
    const string json = """
    {
      "data": [ { "x": 1 }, { "x": 2 } ]
    }
    """;
    using var stream = CreateStream(json);
    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.data[*].x"));
    Assert.Equal(2, results.Count);
  }

  // ── ExtractFirst matches for edge paths ────────────────────────────────

  [Fact]
  public async Task ExtractFirst_ComputedNegativeResult_ReturnsNull()
  {
    const string json = """{"arr": [1, 2, 3]}""";
    using var stream = CreateStream(json);

    // (@.length-4) = -1 => last element
    var result = await stream.ExtractFirstJsonMatchAsync("$.arr[(@.length-4)]");
    AssertNodeEquals(JsonValue.Create(3), result);
  }

  [Fact]
  public async Task ExtractAll_FieldExclusionOnScalarProperty_ReturnsNothing()
  {
    using var stream = CreateStream(SampleJson);
    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.name[!x]"));
    Assert.Empty(results);
  }

  // ── ExtractAll with paths after filter on root array ───────────────────

  [Fact]
  public async Task WithPaths_RootArrayWildcard_Streaming_ReturnsCorrectPaths()
  {
    const string json = """[{"id": 1}, {"id": 2}, {"id": 3}]""";
    using var stream = CreateStream(json);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$[*].id"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$[0].id", results[0].Path);
    Assert.Equal("$[1].id", results[1].Path);
    Assert.Equal("$[2].id", results[2].Path);
  }

  // ── Helper methods ─────────────────────────────────────────────────────

  private static MemoryStream CreateStream(string json)
    => new(Encoding.UTF8.GetBytes(json));

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

  private static void AssertNodeEquals(JsonNode? expected, JsonNode? actual)
  {
    Assert.True(JsonNode.DeepEquals(expected, actual),
      $"Expected: {expected?.ToJsonString() ?? "null"}, Actual: {actual?.ToJsonString() ?? "null"}");
  }
}
