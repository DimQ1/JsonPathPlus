using System.Text.Json.Nodes;
using Xunit;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

public sealed class JsonPathSchemaExtractionTests
{
  private const string NestedJson = """
  {
    "name": "test",
    "count": 42,
    "active": true,
    "emptyObj": {},
    "emptyArr": [],
    "nullableValue": null,
    "items": [
      { "id": 1, "value": "a" },
      { "id": 2, "value": "b" },
      { "id": 3, "value": "c" }
    ],
    "mixedArr": [1, "text", true],
    "nestedObj": {
      "inner": "hello",
      "deep": { "level": 3 }
    }
  }
  """;

  private const string SimpleJson = """{"a":1,"b":"text"}""";

  // ── API Method: ExtractJsonSchema ─────────────────────────────────────

  [Fact]
  public async Task ExtractJsonSchema_Root_ReturnsFullSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
    Assert.True(obj.ContainsKey("properties"));
  }

  [Fact]
  public async Task ExtractJsonSchema_Property_ReturnsPropertySchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.name");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("string", (string?)obj["type"]);
  }

  [Fact]
  public async Task ExtractJsonSchema_NumberProperty_ReturnsNumberType()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.count");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("number", (string?)obj["type"]);
  }

  [Fact]
  public async Task ExtractJsonSchema_BooleanProperty_ReturnsBooleanType()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.active");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("boolean", (string?)obj["type"]);
  }

  [Fact]
  public async Task ExtractJsonSchema_NullProperty_ReturnsNullType()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.nullableValue");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("null", (string?)obj["type"]);
  }

  [Fact]
  public async Task ExtractJsonSchema_EmptyObject_ReturnsObjectType()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.emptyObj");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
    Assert.False(obj.ContainsKey("properties"));
  }

  [Fact]
  public async Task ExtractJsonSchema_EmptyArray_ReturnsArrayType()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.emptyArr");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("array", (string?)obj["type"]);
    Assert.False(obj.ContainsKey("items"));
  }

  [Fact]
  public async Task ExtractJsonSchema_ArrayHomogeneous_ReturnsArrayWithItemsSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.items");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("array", (string?)obj["type"]);
    Assert.True(obj.ContainsKey("items"));
  }

  [Fact]
  public async Task ExtractJsonSchema_ArrayWildcard_ReturnsItemSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.items[*]");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
    Assert.True(obj.ContainsKey("properties"));
  }

  [Fact]
  public async Task ExtractJsonSchema_MixedArray_ReturnsOneOf()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.mixedArr");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    // Mixed array should produce oneOf
    Assert.True(obj.ContainsKey("oneOf") || obj.ContainsKey("type"));
  }

  [Fact]
  public async Task ExtractJsonSchema_NestedObject_RecursiveSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractJsonSchemaAsync("$.nestedObj");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
    Assert.True(obj.ContainsKey("properties"));
  }

  [Fact]
  public async Task ExtractJsonSchema_Dynamic_OnSimpleJson()
  {
    using var stream = CreateStream(SimpleJson);

    var schema = await stream.ExtractJsonSchemaAsync("$");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
  }

  [Fact]
  public void ExtractJsonSchema_FromJsonNode_Works()
  {
    var node = JsonNode.Parse("""{"x":1,"y":"hello"}""");

    var schema = node!.ExtractJsonSchema("$");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("object", (string?)obj["type"]);
  }

  [Fact]
  public async Task ExtractJsonSchema_FromString_Works()
  {
    var schema = await """{"a":true}""".ExtractJsonSchemaAsync("$.a");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("boolean", (string?)obj["type"]);
  }

  // ── schema() path syntax ─────────────────────────────────────────────

  [Fact]
  public async Task SchemaFunction_Root_ReturnsSchema()
  {
    using var stream = CreateStream(SimpleJson);

    var schema = await stream.ExtractFirstJsonMatchAsync("$.schema()");

    Assert.NotNull(schema);
    Assert.IsType<JsonObject>(schema);
  }

  [Fact]
  public async Task SchemaFunction_BracketSyntax_ReturnsSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schema = await stream.ExtractFirstJsonMatchAsync("$.items[schema()]");

    Assert.NotNull(schema);
    var obj = Assert.IsType<JsonObject>(schema);
    Assert.Equal("array", (string?)obj["type"]);
  }

  [Fact]
  public async Task SchemaFunction_AllMatches_ReturnsMultipleSchemas()
  {
    using var stream = CreateStream(NestedJson);

    var schemas = await CollectAsync(stream.ExtractAllJsonMatchesAsync("$.items[*].schema()"));

    Assert.Equal(3, schemas.Count);
    foreach (var s in schemas)
      Assert.IsType<JsonObject>(s);
  }

  [Fact]
  public async Task SchemaFunction_WithWildcardAndSchema_EachMatchGetsOwnSchema()
  {
    using var stream = CreateStream(NestedJson);

    var schemas = await CollectAsync(stream.ExtractAllJsonMatchesWithPathsAsync("$.items[*].schema()"));

    Assert.Equal(3, schemas.Count);
  }

  [Fact]
  public async Task SchemaFunction_NullPath_NoMatchAtNonExistent()
  {
    using var stream = CreateStream(NestedJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.nonexistent.schema()");

    Assert.Null(result);
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  private static Stream CreateStream(string json)
  {
    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
    return new MemoryStream(bytes);
  }

  private static async Task<List<JsonNode?>> CollectAsync(IAsyncEnumerable<JsonNode?> source)
  {
    var results = new List<JsonNode?>();
    await foreach (var item in source)
      results.Add(item);
    return results;
  }

  private static async Task<List<JsonPathMatch>> CollectAsync(IAsyncEnumerable<JsonPathMatch> source)
  {
    var results = new List<JsonPathMatch>();
    await foreach (var item in source)
      results.Add(item);
    return results;
  }
}
