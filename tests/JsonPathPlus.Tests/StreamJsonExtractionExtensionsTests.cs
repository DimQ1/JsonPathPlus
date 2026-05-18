using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

public sealed class StreamJsonExtractionExtensionsTests
{
  private const string SampleJson = """
  {
    "name": "rootName",
    "a": { "b": { "c": 123 } },
    "items": [
      { "id": 1, "value": "a" },
      { "id": 2, "value": "b" },
      { "id": 3, "value": "c" },
      { "id": 4, "value": "d" }
    ],
    "obj": { "p1": "v1", "p2": "v2" },
    "books": [
      { "title": "b1", "price": 5, "isbn": "x", "meta": { "published": 2001 } },
      { "title": "b2", "price": 15, "meta": { "published": 1999 } },
      { "title": "b3", "price": 8, "isbn": "y", "meta": { "published": 2010 } }
    ],
    "nested": {
      "propertyName": "top",
      "inner": {
        "propertyName": "deep",
        "arr": [
          { "propertyName": "arr0" },
          { "other": true }
        ]
      }
    }
  }
  """;

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithNullPath_ReturnsRoot()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync(null);

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithRootPath_ReturnsRoot()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$");

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithPropertyPath_ReturnsPropertyValue()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.name");

    AssertNodeEquals(JsonValue.Create("rootName"), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithDotChainPath_ReturnsNestedValue()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.a.b.c");

    AssertNodeEquals(JsonValue.Create(123), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithArrayIndexPath_ReturnsIndexedElement()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[2].id");

    AssertNodeEquals(JsonValue.Create(3), result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayRangePath_ReturnsRange()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[1:3].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(2), results[0]);
    AssertNodeEquals(JsonValue.Create(3), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithOpenStartRange_ReturnsFromBeginning()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[:2].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(2), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithOpenEndRange_ReturnsToEnd()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[2:].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(3), results[0]);
    AssertNodeEquals(JsonValue.Create(4), results[1]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithNegativeArrayIndex_ReturnsLastElement()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[-1].id");

    AssertNodeEquals(JsonValue.Create(4), result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithNegativeOpenEndRange_ReturnsTrailingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[-2:].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(3), results[0]);
    AssertNodeEquals(JsonValue.Create(4), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithNegativeBoundedRange_ReturnsExpectedSlice()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[-4:-1].id"));

    Assert.Equal(3, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(2), results[1]);
    AssertNodeEquals(JsonValue.Create(3), results[2]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithOutOfRangeNegativeIndex_ReturnsNull()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[-99].id");

    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithComputedLengthMinusOne_ReturnsLastElement()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length-1)].id");

    AssertNodeEquals(JsonValue.Create(4), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithComputedLengthDivision_ReturnsExpectedElement()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length/2)].id");

    AssertNodeEquals(JsonValue.Create(3), result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayWildcard_ReturnsAllElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[*].id"));

    Assert.Equal(4, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(2), results[1]);
    AssertNodeEquals(JsonValue.Create(3), results[2]);
    AssertNodeEquals(JsonValue.Create(4), results[3]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayUnionIndices_ReturnsSpecifiedElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[0,2,3].id"));

    Assert.Equal(3, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(3), results[1]);
    AssertNodeEquals(JsonValue.Create(4), results[2]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayUnionContainingNegativeIndex_ReturnsSpecifiedElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[0,-1].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(4), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithObjectWildcard_ReturnsAllPropertyValues()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj.*"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("v1"), results[0]);
    AssertNodeEquals(JsonValue.Create("v2"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithPropertyUnion_ReturnsSpecifiedPropertiesInOrder()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj[p2,p1]"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("v2"), results[0]);
    AssertNodeEquals(JsonValue.Create("v1"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterExistence_ReturnsMatchingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.isbn)].title"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("b1"), results[0]);
    AssertNodeEquals(JsonValue.Create("b3"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterComparison_ReturnsMatchingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.price < 10)].title"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("b1"), results[0]);
    AssertNodeEquals(JsonValue.Create("b3"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterLogicalAnd_ReturnsMatchingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.price > 1 && @.price < 10)].title"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("b1"), results[0]);
    AssertNodeEquals(JsonValue.Create("b3"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterLogicalNot_ReturnsMatchingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(!@.isbn)].title"));

    Assert.Single(results);
    AssertNodeEquals(JsonValue.Create("b2"), results[0]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterNestedPath_ReturnsMatchingElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.meta.published > 2000)].title"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("b1"), results[0]);
    AssertNodeEquals(JsonValue.Create("b3"), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithRecursiveDescent_ReturnsMatchesAtAnyDepth()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$..propertyName"));

    Assert.Equal(3, results.Count);
    AssertNodeEquals(JsonValue.Create("top"), results[0]);
    AssertNodeEquals(JsonValue.Create("deep"), results[1]);
    AssertNodeEquals(JsonValue.Create("arr0"), results[2]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithRecursiveWildcard_ReturnsFirstRecursiveMatch()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$..*");

    AssertNodeEquals(JsonValue.Create("rootName"), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithNotFoundPath_ReturnsNull()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.does.not.exist");

    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithNotFoundPath_ReturnsEmpty()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.does.not.exist"));

    Assert.Empty(results);
  }

  private static MemoryStream CreateStream(string json)
    => new(Encoding.UTF8.GetBytes(json));

  private static async Task<List<JsonNode?>> CollectAsync(IAsyncEnumerable<JsonNode?> source)
  {
    var list = new List<JsonNode?>();
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
