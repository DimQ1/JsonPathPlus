using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

/// <summary>
/// Tests for ExtractAllJsonMatchesWithPathsAsync covering path-tracking
/// for all segment types: property, array index, array range, array union,
/// property union, filter, computed index, wildcard, recursive descent,
/// field projection, field exclusion, field existence, field count, nested query.
/// </summary>
public sealed class StreamJsonWithPathsTests
{
  private const string TestJson = """
  {
    "name": "root",
    "a": { "b": { "c": 123 } },
    "items": [
      { "id": 1, "value": "a" },
      { "id": 2, "value": "b" },
      { "id": 3, "value": "c" },
      { "id": 4, "value": "d" }
    ],
    "obj": { "p1": "v1", "p2": "v2" },
    "books": [
      { "title": "b1", "author": "Author One", "price": 5, "isbn": "x" },
      { "title": "b2", "author": "Author Two", "price": 15 },
      { "title": "b3", "author": "Author Three", "price": 8, "isbn": "y" }
    ],
    "nested": {
      "propertyName": "top",
      "inner": { "propertyName": "deep" }
    }
  }
  """;

  // ── Property path tracking ─────────────────────────────────────────────

  [Fact]
  public async Task WithPaths_Property_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.name"));

    Assert.Single(results);
    Assert.Equal("$.name", results[0].Path);
    AssertNodeEquals(JsonValue.Create("root"), results[0].Value);
  }

  [Fact]
  public async Task WithPaths_NestedProperty_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.a.b.c"));

    Assert.Single(results);
    Assert.Equal("$.a.b.c", results[0].Path);
    AssertNodeEquals(JsonValue.Create(123), results[0].Value);
  }

  // ── Array index path tracking ──────────────────────────────────────────

  [Fact]
  public async Task WithPaths_ArrayIndex_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[2].id"));

    Assert.Single(results);
    Assert.Equal("$.items[2].id", results[0].Path);
    AssertNodeEquals(JsonValue.Create(3), results[0].Value);
  }

  [Fact]
  public async Task WithPaths_ArrayIndexNegative_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[-1].id"));

    Assert.Single(results);
    Assert.Equal("$.items[3].id", results[0].Path);
    AssertNodeEquals(JsonValue.Create(4), results[0].Value);
  }

  // ── Array range path tracking ──────────────────────────────────────────

  [Fact]
  public async Task WithPaths_ArrayRange_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[1:3].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[1].id", results[0].Path);
    Assert.Equal("$.items[2].id", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_ArrayRangeOpenEnd_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[2:].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[2].id", results[0].Path);
    Assert.Equal("$.items[3].id", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_ArrayRangeOpenStart_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[:2].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[0].id", results[0].Path);
    Assert.Equal("$.items[1].id", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_ArrayRangeNegative_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[-2:].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[2].id", results[0].Path);
    Assert.Equal("$.items[3].id", results[1].Path);
  }

  // ── Array union path tracking ──────────────────────────────────────────

  [Fact]
  public async Task WithPaths_ArrayUnion_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[0,2,3].id"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$.items[0].id", results[0].Path);
    Assert.Equal("$.items[2].id", results[1].Path);
    Assert.Equal("$.items[3].id", results[2].Path);
  }

  [Fact]
  public async Task WithPaths_ArrayUnionNegative_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[0,-1].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[0].id", results[0].Path);
    Assert.Equal("$.items[3].id", results[1].Path);
  }

  // ── Property union path tracking ───────────────────────────────────────

  [Fact]
  public async Task WithPaths_PropertyUnion_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.obj['p1','p2']"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.obj.p1", results[0].Path);
    Assert.Equal("$.obj.p2", results[1].Path);
  }

  // ── Wildcard path tracking ─────────────────────────────────────────────

  [Fact]
  public async Task WithPaths_WildcardOnArray_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[*].id"));

    Assert.Equal(4, results.Count);
    Assert.Equal("$.items[0].id", results[0].Path);
    Assert.Equal("$.items[1].id", results[1].Path);
    Assert.Equal("$.items[2].id", results[2].Path);
    Assert.Equal("$.items[3].id", results[3].Path);
  }

  [Fact]
  public async Task WithPaths_WildcardOnObject_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.obj.*"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.obj.p1", results[0].Path);
    Assert.Equal("$.obj.p2", results[1].Path);
  }

  // ── Recursive descent path tracking ────────────────────────────────────

  [Fact]
  public async Task WithPaths_RecursiveDescentProperty_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$..propertyName"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.nested.propertyName", results[0].Path);
    Assert.Equal("$.nested.inner.propertyName", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_RecursiveDescentWildcard_ReturnsMatches()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$..*"));

    // Should find many values at all depths
    Assert.True(results.Count > 5, $"Expected > 5 matches, got {results.Count}");
    Assert.All(results, r => Assert.NotNull(r.Value));
  }

  // ── Filter path tracking ───────────────────────────────────────────────

  [Fact]
  public async Task WithPaths_Filter_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.isbn)].title"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.books[0].title", results[0].Path);
    Assert.Equal("$.books[2].title", results[1].Path);
  }

  [Fact]
  public async Task WithPaths_FilterComparison_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 10)].title"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.books[0].title", results[0].Path);
    Assert.Equal("$.books[2].title", results[1].Path);
  }

  // ── Computed index path tracking ───────────────────────────────────────

  [Fact]
  public async Task WithPaths_ComputedIndex_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.items[(@.length-1)].id"));

    Assert.Single(results);
    Assert.Equal("$.items[3].id", results[0].Path);
    AssertNodeEquals(JsonValue.Create(4), results[0].Value);
  }

  // ── Field projection path tracking ─────────────────────────────────────

  [Fact]
  public async Task WithPaths_FieldProjection_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[title, price]"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$.books[0]", results[0].Path);
    Assert.Equal("$.books[1]", results[1].Path);
    Assert.Equal("$.books[2]", results[2].Path);
  }

  [Fact]
  public async Task WithPaths_FilterAndFieldProjection_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 10)][title, author]"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.books[0]", results[0].Path);
    Assert.Equal("$.books[2]", results[1].Path);
  }

  // ── Field exclusion path tracking ──────────────────────────────────────

  [Fact]
  public async Task WithPaths_FieldExclusion_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[0][!title, !price]"));

    Assert.Single(results);
    Assert.Equal("$.books[0]", results[0].Path);
    var obj = results[0].Value as JsonObject;
    Assert.NotNull(obj);
    Assert.False(obj!.ContainsKey("title"));
    Assert.False(obj!.ContainsKey("price"));
    Assert.True(obj!.ContainsKey("author"));
  }

  [Fact]
  public async Task WithPaths_FilterAndFieldExclusion_ReturnsCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price > 2)][!title, !author]"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$.books[0]", results[0].Path);
    Assert.Equal("$.books[1]", results[1].Path);
    Assert.Equal("$.books[2]", results[2].Path);
    foreach (var r in results)
    {
      var obj = r.Value as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("title"));
      Assert.False(obj!.ContainsKey("author"));
    }
  }

  // ── Field existence path tracking ──────────────────────────────────────

  [Fact]
  public async Task WithPaths_FieldExistence_ReturnsBoolWithCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 20)][exist(title)]"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$.books[0]", results[0].Path);
    Assert.Equal("$.books[1]", results[1].Path);
    Assert.Equal("$.books[2]", results[2].Path);
    Assert.All(results, r => AssertNodeEquals(JsonValue.Create(true), r.Value));
  }

  [Fact]
  public async Task WithPaths_FieldExistenceWildcard_ReturnsBoolWithCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 20)][exist(*)]"));

    Assert.Equal(3, results.Count);
    Assert.Equal("$.books[0]", results[0].Path);
    Assert.Equal("$.books[1]", results[1].Path);
    Assert.Equal("$.books[2]", results[2].Path);
    Assert.All(results, r => AssertNodeEquals(JsonValue.Create(true), r.Value));
  }

  // ── Field count path tracking ──────────────────────────────────────────

  [Fact]
  public async Task WithPaths_FieldCount_ReturnsCountWithCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 20)][count(title)]"));

    Assert.Equal(3, results.Count);
    Assert.All(results, r => AssertNodeEquals(JsonValue.Create(1), r.Value));
  }

  [Fact]
  public async Task WithPaths_FieldCountWildcard_ReturnsCountWithCorrectPaths()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.books[?(@.price < 20)][count(*)]"));

    Assert.Equal(3, results.Count);
    // book[0] has 4 fields (title, author, price, isbn), book[1] has 3, book[2] has 4
    AssertNodeEquals(JsonValue.Create(4), results[0].Value);
    AssertNodeEquals(JsonValue.Create(3), results[1].Value);
    AssertNodeEquals(JsonValue.Create(4), results[2].Value);
  }

  // ── Nested query path tracking ─────────────────────────────────────────

  [Fact]
  public async Task WithPaths_NestedQuery_ReturnsCorrectPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync(
        "$[books[?(@.price < 20)][title, author], name]"));

    Assert.Single(results);
    Assert.Equal("$", results[0].Path);
    var obj = results[0].Value as JsonObject;
    Assert.NotNull(obj);
    Assert.Equal("root", obj!["name"]?.GetValue<string>());
  }

  // ── Root path ──────────────────────────────────────────────────────────

  [Fact]
  public async Task WithPaths_Root_ReturnsRootPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$"));

    Assert.Single(results);
    Assert.Equal("$", results[0].Path);
    AssertNodeEquals(JsonNode.Parse(TestJson), results[0].Value);
  }

  [Fact]
  public async Task WithPaths_Null_ReturnsRootPath()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync(null));

    Assert.Single(results);
    Assert.Equal("$", results[0].Path);
  }

  // ── Not-found ──────────────────────────────────────────────────────────

  [Fact]
  public async Task WithPaths_NotFound_ReturnsEmpty()
  {
    using var stream = CreateStream(TestJson);
    var results = await CollectPathMatchesAsync(
      stream.ExtractAllJsonMatchesWithPathsAsync("$.does.not.exist"));

    Assert.Empty(results);
  }

  // ── Helper methods ─────────────────────────────────────────────────────

  private static MemoryStream CreateStream(string json)
    => new(Encoding.UTF8.GetBytes(json));

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
