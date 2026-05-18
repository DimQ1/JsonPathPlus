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
      { "title": "b1", "author": "Author One", "price": 5, "isbn": "x", "meta": { "published": 2001 } },
      { "title": "b2", "author": "Author Two", "price": 15, "meta": { "published": 1999 } },
      { "title": "b3", "author": "Author Three", "price": 8, "isbn": "y", "meta": { "published": 2010 } }
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

  private const string RootArrayJson = """
  [
    { "id": 1, "name": "one" },
    { "id": 2, "name": "two" },
    { "id": 3, "name": "three" },
    { "id": 4, "name": "four" }
  ]
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
  public async Task ExtractAllJsonMatchesWithPathsAsync_WithArrayRangePath_ReturnsValuesAndPaths()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectPathMatchesAsync(stream.ExtractAllJsonMatchesWithPathsAsync("$.items[1:3].id"));

    Assert.Equal(2, results.Count);
    Assert.Equal("$.items[1].id", results[0].Path);
    Assert.Equal("$.items[2].id", results[1].Path);
    AssertNodeEquals(JsonValue.Create(2), results[0].Value);
    AssertNodeEquals(JsonValue.Create(3), results[1].Value);
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

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj['p2','p1']"));

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
  public async Task ExtractAllJsonMatchesAsync_WithFieldProjection_ReturnsProjectedObjects()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[title, price]"));

    Assert.Equal(3, results.Count);
    
    var result1 = results[0] as JsonObject;
    Assert.NotNull(result1);
    Assert.Equal("b1", result1!["title"]?.GetValue<string>());
    Assert.Equal(5, result1!["price"]?.GetValue<int>());
    Assert.Null(result1!["isbn"]);
    
    var result3 = results[2] as JsonObject;
    Assert.NotNull(result3);
    Assert.Equal("b3", result3!["title"]?.GetValue<string>());
    Assert.Equal(8, result3!["price"]?.GetValue<int>());
    Assert.Null(result3!["isbn"]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterAndFieldProjection_ReturnsFilteredAndProjectedObjects()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.price > 2)][title, author]"));

    // All books have price > 2
    Assert.Equal(3, results.Count);
    
    var result1 = results[0] as JsonObject;
    Assert.NotNull(result1);
    Assert.Equal("b1", result1!["title"]?.GetValue<string>());
    Assert.Equal("Author One", result1!["author"]?.GetValue<string>());
    Assert.Null(result1!["price"]); // price should not be in projection
    
    var result2 = results[1] as JsonObject;
    Assert.NotNull(result2);
    Assert.Equal("b2", result2!["title"]?.GetValue<string>());
    Assert.Equal("Author Two", result2!["author"]?.GetValue<string>());
    
    var result3 = results[2] as JsonObject;
    Assert.NotNull(result3);
    Assert.Equal("b3", result3!["title"]?.GetValue<string>());
    Assert.Equal("Author Three", result3!["author"]?.GetValue<string>());
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFieldProjectionAndWildcard_ReturnsProjectedObjects()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[id, value]"));

    Assert.Equal(4, results.Count);
    
    var result1 = results[0] as JsonObject;
    Assert.NotNull(result1);
    Assert.Equal(1, result1!["id"]?.GetValue<int>());
    Assert.Equal("a", result1!["value"]?.GetValue<string>());
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFieldProjectionMissingFields_SkipsAbsentFields()
  {
    using var stream = CreateStream(SampleJson);

    // isbn field is not present in book[1]
    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[title, isbn]"));

    Assert.Equal(3, results.Count);
    
    var result2 = results[1] as JsonObject;
    Assert.NotNull(result2);
    Assert.Equal("b2", result2!["title"]?.GetValue<string>());
    Assert.False(result2!.ContainsKey("isbn")); // isbn not present in book[1]
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithSingleFieldProjection_ReturnsProjectedObjects()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[title]"));

    Assert.Equal(3, results.Count);

    var result1 = results[0] as JsonObject;
    Assert.NotNull(result1);
    Assert.Equal("b1", result1!["title"]?.GetValue<string>());
    Assert.Single(result1!);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithSingleFieldProjectionOnObject_ReturnsProjectedObject()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj[p1]"));

    Assert.Single(results);
    var result = results[0] as JsonObject;
    Assert.NotNull(result);
    Assert.Equal("v1", result!["p1"]?.GetValue<string>());
    Assert.Single(result!);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithStoreBookFilterAndSingleProjection_ReturnsProjectedTitleObjects()
  {
    const string storeJson = """
    {
      "store": {
        "name": "City Books",
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 },
          { "title": "Clean Code", "price": 34.99 },
          { "title": "The Hobbit", "price": 12.49 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price < 20)][title]"));

    Assert.Equal(2, results.Count);

    var first = results[0] as JsonObject;
    Assert.NotNull(first);
    Assert.Equal("The Great Gatsby", first!["title"]?.GetValue<string>());
    Assert.Single(first!);

    var second = results[1] as JsonObject;
    Assert.NotNull(second);
    Assert.Equal("The Hobbit", second!["title"]?.GetValue<string>());
    Assert.Single(second!);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithStoreNameSingleProjection_ReturnsProjectedObject()
  {
    const string storeJson = """
    {
      "store": {
        "name": "City Books",
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store[name]"));

    Assert.Single(results);
    var result = results[0] as JsonObject;
    Assert.NotNull(result);
    Assert.Equal("City Books", result!["name"]?.GetValue<string>());
    Assert.Single(result!);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithExistFunctionOnFilteredBooks_ReturnsBoolPerMatch()
  {
    const string storeJson = """
    {
      "store": {
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 },
          { "title": "Clean Code", "price": 34.99 },
          { "title": "The Hobbit", "price": 12.49 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price < 20)][exist(title)]"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(true), results[0]);
    AssertNodeEquals(JsonValue.Create(true), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithCountFunctionOnFilteredBooks_ReturnsNumberPerMatch()
  {
    const string storeJson = """
    {
      "store": {
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 },
          { "title": "Clean Code", "price": 34.99 },
          { "title": "The Hobbit", "price": 12.49 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price < 20)][count(title)]"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(1), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithExistWildcardOnFilteredBooks_ReturnsBoolPerMatch()
  {
    const string storeJson = """
    {
      "store": {
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 },
          { "title": "Clean Code", "price": 34.99 },
          { "title": "The Hobbit", "price": 12.49 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price < 20)][exist(*)]"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(true), results[0]);
    AssertNodeEquals(JsonValue.Create(true), results[1]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithCountWildcardOnFilteredBooks_ReturnsFieldCountPerMatch()
  {
    const string storeJson = """
    {
      "store": {
        "book": [
          { "title": "The Great Gatsby", "price": 8.99 },
          { "title": "Clean Code", "price": 34.99 },
          { "title": "The Hobbit", "price": 12.49 }
        ]
      }
    }
    """;

    using var stream = CreateStream(storeJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price < 20)][count(*)]"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(2), results[0]);
    AssertNodeEquals(JsonValue.Create(2), results[1]);
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

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_OnJsonNode_MatchesStreamBehavior()
  {
    using var stream = CreateStream(SampleJson);
    var node = JsonNode.Parse(SampleJson);

    var expected = await stream.ExtractFirstJsonMatchAsync("$.a.b.c");
    var actual = await node!.ExtractFirstJsonMatchAsync("$.a.b.c");

    AssertNodeEquals(expected, actual);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_OnJsonNode_MatchesStreamBehavior()
  {
    using var stream = CreateStream(SampleJson);
    var node = JsonNode.Parse(SampleJson);

    var expected = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[1:3].id"));
    var actual = await CollectAsync(node!.ExtractAllJsonMatchesAsync("$.items[1:3].id"));

    Assert.Equal(expected.Count, actual.Count);
    for (var i = 0; i < expected.Count; i++)
      AssertNodeEquals(expected[i], actual[i]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_OnString_MatchesStreamBehavior()
  {
    using var stream = CreateStream(SampleJson);

    var expected = await stream.ExtractFirstJsonMatchAsync("$.books[?(@.price < 10)].title");
    var actual = await SampleJson.ExtractFirstJsonMatchAsync("$.books[?(@.price < 10)].title");

    AssertNodeEquals(expected, actual);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_OnString_MatchesStreamBehavior()
  {
    using var stream = CreateStream(SampleJson);

    var expected = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj['p2','p1']"));
    var actual = await CollectAsync(SampleJson.ExtractAllJsonMatchesAsync("$.obj['p2','p1']"));

    Assert.Equal(expected.Count, actual.Count);
    for (var i = 0; i < expected.Count; i++)
      AssertNodeEquals(expected[i], actual[i]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFieldProjectionOnObject_ReturnsProjectedObject()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj[p2, p1]"));

    // Field projection on a single object returns 1 projected object
    Assert.Single(results);
    
    var result = results[0] as JsonObject;
    Assert.NotNull(result);
    Assert.Equal("v2", result!["p2"]?.GetValue<string>());
    Assert.Equal("v1", result!["p1"]?.GetValue<string>());
    Assert.False(result!.ContainsKey("obj")); // Should not have other properties
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithRootArrayWildcard_UsesStreamingAndReturnsAll()
  {
    using var stream = CreateStream(RootArrayJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$[*].id"));

    Assert.Equal(4, results.Count);
    AssertNodeEquals(JsonValue.Create(1), results[0]);
    AssertNodeEquals(JsonValue.Create(2), results[1]);
    AssertNodeEquals(JsonValue.Create(3), results[2]);
    AssertNodeEquals(JsonValue.Create(4), results[3]);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesWithPathsAsync_WithRootArrayWildcard_UsesStreamingAndReturnsPaths()
  {
    using var stream = CreateStream(RootArrayJson);

    var results = await CollectPathMatchesAsync(stream.ExtractAllJsonMatchesWithPathsAsync("$[*].id"));

    Assert.Equal(4, results.Count);
    Assert.Equal("$[0].id", results[0].Path);
    Assert.Equal("$[1].id", results[1].Path);
    Assert.Equal("$[2].id", results[2].Path);
    Assert.Equal("$[3].id", results[3].Path);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithRootArrayRange_UsesStreamingAndReturnsRange()
  {
    using var stream = CreateStream(RootArrayJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$[1:3].id"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create(2), results[0]);
    AssertNodeEquals(JsonValue.Create(3), results[1]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithRootArrayIndex_UsesStreamingAndReturnsFirstMatch()
  {
    using var stream = CreateStream(RootArrayJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$[2].id");

    AssertNodeEquals(JsonValue.Create(3), result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithRootArrayFilter_UsesStreamingAndReturnsMatches()
  {
    using var stream = CreateStream(RootArrayJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$[?(@.id >= 3)].name"));

    Assert.Equal(2, results.Count);
    AssertNodeEquals(JsonValue.Create("three"), results[0]);
    AssertNodeEquals(JsonValue.Create("four"), results[1]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithRootArrayNegativeIndex_FallsBackAndReturnsExpected()
  {
    using var stream = CreateStream(RootArrayJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$[-1].id");

    AssertNodeEquals(JsonValue.Create(4), result);
  }

  // ── Malformed / edge-case path tests ──────────────────────────────────────

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_UnclosedBracketMidPath_DoesNotThrowReturnsPartialMatch()
  {
    // Parser discards the unclosed [p2,p1 segment; segments = [Property("obj")]
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.obj[p2,p1");

    Assert.IsType<System.Text.Json.Nodes.JsonObject>(result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_UnclosedBracketMidPath_DoesNotThrowReturnsPartialMatches()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj[p2,p1"));

    Assert.Single(results);
    Assert.IsType<System.Text.Json.Nodes.JsonObject>(results[0]);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_UnclosedBracketAtRoot_DoesNotThrowReturnsRoot()
  {
    // $[name has no closing ]; bracket segment is discarded, segments = [] → root
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$[name");

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_TrailingDot_DoesNotThrowReturnsProperty()
  {
    // $.name. — trailing dot is consumed harmlessly; segments = [Property("name")]
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.name.");

    AssertNodeEquals(JsonValue.Create("rootName"), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_OnlyDoubleDot_DoesNotThrowReturnsRoot()
  {
    // $.. — recursive segment with empty name is skipped; segments = [] → root
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$..");

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_EmptyStringPath_DoesNotThrowReturnsRoot()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("");

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_EmptyFilterExpression_DoesNotThrowReturnsArray()
  {
    // $.items[?()] — filter body is too short; bracket segment discarded; segments = [Property("items")]
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[?()]");

    Assert.IsType<System.Text.Json.Nodes.JsonArray>(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_InvalidBracketContent_DoesNotThrowReturnsArray()
  {
    // $.items[not-an-index] — invalid identifier for projection, so bracket segment is discarded
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[not-an-index]");

    Assert.IsType<System.Text.Json.Nodes.JsonArray>(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_FilterWithoutOpenParenthesis_DoesNotThrowReturnsArray()
  {
    // $.items[?@.isbn] — doesn't match ?( ... ) pattern; segment discarded
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[?@.isbn]");

    Assert.IsType<System.Text.Json.Nodes.JsonArray>(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_DoubleDollarSign_DoesNotThrowReturnsNull()
  {
    // $$ — second $ is treated as property name "$"; no such property → null
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$$");

    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WhitespaceOnlyPropertyName_DoesNotThrowReturnsNull()
  {
    // "$.   " — property name is three spaces; no match → null
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.   ");

    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_UnclosedBracketOnRecursiveDescent_DoesNotThrow()
  {
    // $..[id — recursive bracket has no closing ]; segment discarded → root
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$..[id");

    AssertNodeEquals(JsonNode.Parse(SampleJson), result);
  }

  // ── Field exclusion tests ────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithFieldExclusion_ExcludesSpecifiedFields()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.books[0][!title, !price]");

    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    Assert.NotNull(obj);
    Assert.False(obj!.ContainsKey("title"));
    Assert.False(obj!.ContainsKey("price"));
    Assert.True(obj!.ContainsKey("author"));
    Assert.True(obj!.ContainsKey("isbn"));
    Assert.True(obj!.ContainsKey("meta"));
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithFilterAndFieldExclusion_ReturnsFilteredWithExcludedFields()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[?(@.price > 2)][!title, !price]"));

    Assert.Equal(3, results.Count);
    
    // All results should not have title and price
    foreach (var result in results)
    {
      Assert.IsType<JsonObject>(result);
      var obj = result as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("title"));
      Assert.False(obj!.ContainsKey("price"));
      Assert.True(obj!.ContainsKey("author"));
    }
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithWildcardAndFieldExclusion_ExcludesFieldsFromAllMatches()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[*][!isbn, !meta]"));

    Assert.Equal(3, results.Count);
    
    foreach (var result in results)
    {
      Assert.IsType<JsonObject>(result);
      var obj = result as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("isbn"));
      Assert.False(obj!.ContainsKey("meta"));
      Assert.True(obj!.ContainsKey("title"));
      Assert.True(obj!.ContainsKey("author"));
    }
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayIndexAndFieldExclusion_ExcludesFieldsFromIndexedElement()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[1][!author, !meta]"));

    Assert.Single(results);
    var obj = results[0] as JsonObject;
    Assert.NotNull(obj);
    Assert.False(obj!.ContainsKey("author"));
    Assert.False(obj!.ContainsKey("meta"));
    Assert.True(obj!.ContainsKey("title"));
    Assert.True(obj!.ContainsKey("price"));
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_WithSingleFieldExclusion_ExcludesOnlyThatField()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.books[0][!title]");

    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    Assert.NotNull(obj);
    Assert.False(obj!.ContainsKey("title"));
    Assert.True(obj!.ContainsKey("author"));
    Assert.True(obj!.ContainsKey("price"));
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayRangeAndFieldExclusion_ExcludesFieldsFromRangeElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[0:2][!isbn]"));

    Assert.Equal(2, results.Count);
    foreach (var result in results)
    {
      Assert.IsType<JsonObject>(result);
      var obj = result as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("isbn"));
    }
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithArrayUnionAndFieldExclusion_ExcludesFieldsFromUnionElements()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[0,2][!price, !meta]"));

    Assert.Equal(2, results.Count);
    foreach (var result in results)
    {
      Assert.IsType<JsonObject>(result);
      var obj = result as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("price"));
      Assert.False(obj!.ContainsKey("meta"));
      Assert.True(obj!.ContainsKey("title"));
    }
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_WithPropertyAndFieldExclusion_ExcludesFieldsFromSelectedObject()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.obj[!p2]"));

    Assert.Single(results);
    var obj = results[0] as JsonObject;
    Assert.NotNull(obj);
    Assert.False(obj!.ContainsKey("p2"));
    Assert.True(obj!.ContainsKey("p1"));
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_FieldExclusionOnNonObject_ReturnsNothing()
  {
    using var stream = CreateStream(SampleJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.name[!title]");

    // String is not an object, so field exclusion returns nothing
    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_FieldExclusionExcludingAllFields_ReturnsEmptyObjects()
  {
    using var stream = CreateStream(SampleJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.books[0][!title, !author, !price, !isbn, !category, !meta]"));

    Assert.Single(results);
    var obj = results[0] as JsonObject;
    Assert.NotNull(obj);
    Assert.Empty(obj!); // Object has no properties
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_FieldExclusionWithRootArrayStreaming_ExcludesFields()
  {
    using var stream = CreateStream(RootArrayJson);

    var results = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$[*][!name]"));

    Assert.Equal(4, results.Count);
    foreach (var result in results)
    {
      Assert.IsType<JsonObject>(result);
      var obj = result as JsonObject;
      Assert.NotNull(obj);
      Assert.False(obj!.ContainsKey("name"));
      Assert.True(obj!.ContainsKey("id"));
    }
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

  private static async Task<List<JsonPathMatch>> CollectPathMatchesAsync(IAsyncEnumerable<JsonPathMatch> source)
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
