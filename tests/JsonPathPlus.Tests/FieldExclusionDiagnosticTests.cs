using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

public sealed class FieldExclusionDiagnosticTests
{
  [Fact]
  public void Validator_FieldExclusionPath_IsRecognizedAsValid()
  {
    var result = JsonPathValidator.Validate("$[!title, !price]");
    Assert.True(result.IsValid, $"Expected valid, got error: {result.Error}");
  }

  [Fact]
  public async Task SimpleFieldExclusion_SingleField()
  {
    var json = """{"title": "Test", "author": "John", "price": 10}""";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var result = await stream.ExtractFirstJsonMatchAsync("$[!title]");
    
    Assert.NotNull(result);
    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    Assert.False(obj!.ContainsKey("title"), "Should not have title");
    Assert.True(obj!.ContainsKey("author"), $"Should have author, but got keys: {string.Join(", ", obj.Select(x => x.Key))}");
    Assert.True(obj!.ContainsKey("price"), "Should have price");
  }

  [Fact]
  public async Task SimpleFieldExclusion_MultipleFields()
  {
    var json = """{"title": "Test", "author": "John", "price": 10, "isbn": "123"}""";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var result = await stream.ExtractFirstJsonMatchAsync("$[!title, !price]");
    
    Assert.NotNull(result);
    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    Assert.False(obj!.ContainsKey("title"), "Should not have title");
    Assert.False(obj!.ContainsKey("price"), "Should not have price");
    Assert.True(obj!.ContainsKey("author"), $"Should have author, but got keys: {string.Join(", ", obj.Select(x => x.Key))}");
    Assert.True(obj!.ContainsKey("isbn"), "Should have isbn");
  }

  [Fact]
  public async Task SimpleFieldExclusion_OnArrayElement()
  {
    var json = """[{"title": "Test", "author": "John"},{"title": "Test2", "author": "Jane"}]""";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var result = await stream.ExtractFirstJsonMatchAsync("$[0][!title]");
    
    Assert.NotNull(result);
    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    Assert.False(obj!.ContainsKey("title"), "Should not have title");
    Assert.True(obj!.ContainsKey("author"), $"Should have author, got keys: {string.Join(", ", obj.Select(x => x.Key))}");
  }

  [Fact]
  public async Task FieldExclusion_MatchesSampleJsonBookStructure()
  {
    var json = """
    {
      "books": [
        { "title": "b1", "author": "Author One", "price": 5, "isbn": "x", "meta": { "published": 2001 } },
        { "title": "b2", "author": "Author Two", "price": 15, "meta": { "published": 1999 } },
        { "title": "b3", "author": "Author Three", "price": 8, "isbn": "y", "meta": { "published": 2010 } }
      ]
    }
    """;
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var result = await stream.ExtractFirstJsonMatchAsync("$.books[0][!title, !price]");
    
    Assert.NotNull(result);
    Assert.IsType<JsonObject>(result);
    var obj = result as JsonObject;
    var keys = string.Join(", ", obj!.Select(x => x.Key));
    Assert.False(obj!.ContainsKey("title"), $"Should not have title. Keys: {keys}");
    Assert.False(obj!.ContainsKey("price"), $"Should not have price. Keys: {keys}");
    Assert.True(obj!.ContainsKey("author"), $"Should have author. Keys: {keys}");
    Assert.True(obj!.ContainsKey("isbn"), $"Should have isbn. Keys: {keys}");
    Assert.True(obj!.ContainsKey("meta"), $"Should have meta. Keys: {keys}");
  }
}

