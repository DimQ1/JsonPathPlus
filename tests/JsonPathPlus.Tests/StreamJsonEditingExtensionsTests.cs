using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class StreamJsonEditingExtensionsTests
{
  private const string Source = """
    {"items":[{"id":1,"name":"one","payload":[1,2]},{"id":2,"name":"two","payload":[3]},{"id":3,"name":"three","payload":[]}],"meta":{"total":3},"nil":null}
    """;

  [Fact]
  public async Task Callback_UsesJsonNodeSyntaxAndPreservesNonselectedData()
  {
    using var input = Input(Source);
    using var output = new MemoryStream();
    var count = await input.EditJsonAsync(output, "$.items[?(@.id >= 2)]", node =>
    {
      node!["name"] = "updated";
      node["enabled"] = true;
      node["payload"]!.AsArray().Add(99);
      return node;
    });

    var result = Result(output)!;
    Assert.Equal(2, count);
    Assert.Equal("one", result["items"]![0]!["name"]!.GetValue<string>());
    Assert.Equal("updated", result["items"]![1]!["name"]!.GetValue<string>());
    Assert.True(result["items"]![2]!["enabled"]!.GetValue<bool>());
    Assert.Equal(99, result["items"]![1]!["payload"]![1]!.GetValue<int>());
    Assert.Equal(3, result["meta"]!["total"]!.GetValue<int>());
    Assert.Equal(0, input.Position);
    Assert.True(input.CanRead && output.CanWrite);
  }

  [Theory]
  [InlineData("$[2,0,2,-1]", "[11,2,13]", 2)]
  [InlineData("$[-2:]", "[1,12,13]", 2)]
  [InlineData("$[(@.length-1)]", "[1,2,13]", 1)]
  [InlineData("$[*]", "[11,12,13]", 3)]
  [InlineData("$[?(@ >= 2)]", "[1,12,13]", 2)]
  public async Task ArraySelectors_UseOriginalIndicesAndEditEachLocationOnce(string path, string expected, long expectedCount)
  {
    using var input = new AsyncReadStream("[1,2,3]", 1);
    using var output = new MemoryStream();
    var count = await input.EditJsonAsync(output, path, node => JsonValue.Create(node!.GetValue<int>() + 10));
    Assert.Equal(expectedCount, count);
    AssertJson(expected, output);
    Assert.True(input.CanRead);
  }

  [Fact]
  public async Task RecursiveEditing_SuppressesOverlappingDescendantEdits()
  {
    using var input = Input("""{"target":{"target":{"id":1}},"other":{"target":2}}""");
    using var output = new MemoryStream();
    var count = await input.SetJsonValueAsync(output, "$..target", JsonValue.Create(7));
    Assert.Equal(2, count);
    AssertJson("""{"target":7,"other":{"target":7}}""", output);
  }

  [Theory]
  [InlineData("$.items[?(@.id >= 2)]", """{"items":[{"id":1,"name":"one","payload":[1,2]}],"meta":{"total":3},"nil":null}""", 2)]
  [InlineData("$.items[0,2]", """{"items":[{"id":2,"name":"two","payload":[3]}],"meta":{"total":3},"nil":null}""", 2)]
  [InlineData("$.items[*]", """{"items":[],"meta":{"total":3},"nil":null}""", 3)]
  [InlineData("$.nil", """{"items":[{"id":1,"name":"one","payload":[1,2]},{"id":2,"name":"two","payload":[3]},{"id":3,"name":"three","payload":[]}],"meta":{"total":3}}""", 1)]
  public async Task RemovingMatches_ProducesValidSeparators(string path, string expected, long expectedCount)
  {
    using var input = new AsyncReadStream(Source, 7);
    using var output = new MemoryStream();
    Assert.Equal(expectedCount, await input.RemoveJsonMatchesAsync(output, path));
    AssertJson(expected, output);
  }

  [Fact]
  public async Task CallbackNull_WritesNullRatherThanRemovingTheProperty()
  {
    using var input = Input("""{"a":1,"b":2}""");
    using var output = new MemoryStream();
    Assert.Equal(1, await input.EditJsonAsync(output, "$.a", _ => null));
    AssertJson("""{"a":null,"b":2}""", output);
  }

  [Theory]
  [InlineData("$.items[id,name]", """{"items":[{"id":1,"name":"one"},{"id":2,"name":"two"},{"id":3,"name":"three"}],"meta":{"total":3},"nil":null}""")]
  [InlineData("$.items[!payload]", """{"items":[{"id":1,"name":"one"},{"id":2,"name":"two"},{"id":3,"name":"three"}],"meta":{"total":3},"nil":null}""")]
  [InlineData("$.items[*][id]", """{"items":[{"id":1},{"id":2},{"id":3}],"meta":{"total":3},"nil":null}""")]
  [InlineData("$.items[missing]", """{"items":[{},{},{}],"meta":{"total":3},"nil":null}""")]
  public async Task Projection_RewritesOnlySelectedObjectsWithoutMaterialization(string path, string expected)
  {
    using var input = new AsyncReadStream(Source, 2);
    using var output = new MemoryStream();
    var options = new JsonPathEditingOptions { MaxMaterializedValueBytes = 1 };
    Assert.True(await input.ProjectJsonAsync(output, path, options) > 0);
    AssertJson(expected, output);
  }

  [Fact]
  public async Task RootProjection_PreservesSourceFieldOrderAndNonobjectArrayItems()
  {
    using var input = Input("""[{"name":"a","id":1,"drop":0},null,[{"id":2}],3]""");
    using var output = new MemoryStream();
    Assert.Equal(1, await input.ProjectJsonAsync(output, "$[id,name]"));
    Assert.Equal("""[{"name":"a","id":1},null,[{"id":2}],3]""", Encoding.UTF8.GetString(output.ToArray()));
  }

  [Fact]
  public async Task HugeUnselectedCollection_IsCopiedWithoutInvokingCallbackOrBuildingDom()
  {
    var json = "{\"huge\":[" + string.Join(",", Enumerable.Range(0, 50000)) + "],\"edit\":1}";
    using var input = new AsyncReadStream(json, 127);
    using var output = new MemoryStream();
    var calls = 0;
    var count = await input.EditJsonAsync(output, "$.edit", node =>
    {
      calls++;
      return JsonValue.Create(node!.GetValue<int>() + 1);
    }, new JsonPathEditingOptions { MaxMaterializedValueBytes = 8 });
    Assert.Equal(1, count);
    Assert.Equal(1, calls);
    Assert.Equal(50000, Result(output)!["huge"]!.AsArray().Count);
    Assert.Equal(2, Result(output)!["edit"]!.GetValue<int>());
  }

  [Fact]
  public async Task LargeOuterFilter_ObservesDependenciesAndProjectsNestedItems()
  {
    var items = string.Join(",", Enumerable.Range(0, 3000).Select(i => $"{{\"id\":{i},\"drop\":true}}"));
    using var input = new AsyncReadStream("{\"groups\":[{\"items\":[" + items + "],\"id\":7}]}", 127);
    using var output = new MemoryStream();
    Assert.Equal(1, await input.ProjectJsonAsync(output, "$.groups[?(@.id == 7)].items[id]"));
    var result = Result(output)!["groups"]![0]!["items"]!.AsArray();
    Assert.Equal(3000, result.Count);
    Assert.Equal(2999, result[2999]!["id"]!.GetValue<int>());
    Assert.False(result[2999]!.AsObject().ContainsKey("drop"));
  }

  [Theory]
  [InlineData("null")]
  [InlineData("true")]
  [InlineData("1e+03")]
  [InlineData("\"escaped\\nvalue\"")]
  public async Task RootReplacement_HandlesAllKindsAndBom(string json)
  {
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
    using var input = new MemoryStream(bytes);
    using var output = new MemoryStream();
    Assert.Equal(1, await input.SetJsonValueAsync(output, "$", JsonNode.Parse("""{"replacement":[1,null]}""")));
    AssertJson("""{"replacement":[1,null]}""", output);
  }

  [Fact]
  public async Task NoMatches_CopiesJsonWithoutNormalizingNumericLexemes()
  {
    using var input = Input("""{"number":1e+03,"text":"a\\b"}""");
    using var output = new MemoryStream();
    Assert.Equal(0, await input.SetJsonValueAsync(output, "$.missing", JsonValue.Create(9)));
    Assert.Contains("1e+03", Encoding.UTF8.GetString(output.ToArray()));
    AssertJson("""{"number":1e+03,"text":"a\\b"}""", output);
  }

  [Fact]
  public async Task InPlaceEdits_CanGrowShrinkAndPreservePrefixAndPosition()
  {
    using var stream = new MemoryStream();
    await stream.WriteAsync(Encoding.UTF8.GetBytes("prefix" + Source));
    stream.Position = 6;
    Assert.Equal(1, await stream.EditJsonInPlaceAsync("$.meta", node =>
    {
      node!["new property"] = new string('x', 1000);
      return node;
    }));
    Assert.Equal(6, stream.Position);
    Assert.Equal(1, await stream.ProjectJsonInPlaceAsync("$.meta[total]"));
    Assert.Equal(1, await stream.RemoveJsonMatchesInPlaceAsync("$.nil"));
    Assert.Equal(1, await stream.SetJsonValueInPlaceAsync("$.meta.total", JsonValue.Create(9)));
    Assert.Equal(6, stream.Position);
    Assert.StartsWith("prefix", Encoding.UTF8.GetString(stream.ToArray()));
    var result = JsonNode.Parse(stream.ToArray().AsSpan(6))!;
    Assert.Equal(9, result["meta"]!["total"]!.GetValue<int>());
    Assert.False(result.AsObject().ContainsKey("nil"));
    Assert.Single(result["meta"]!.AsObject());
  }

  [Fact]
  public async Task InPlaceCallbackFailure_PreservesSourceExactly()
  {
    using var input = Input(Source);
    var before = input.ToArray();
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      input.EditJsonInPlaceAsync("$.items[0]", _ => throw new InvalidOperationException("callback failure")));
    Assert.Equal(before, input.ToArray());
    Assert.Equal(0, input.Position);
    Assert.True(input.CanRead);
  }

  [Fact]
  public async Task InPlaceCancellationDuringPreparation_PreservesSourceExactly()
  {
    using var input = Input(Source);
    using var cancellation = new CancellationTokenSource();
    var before = input.ToArray();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      input.EditJsonInPlaceAsync("$.items[*]", node =>
      {
        cancellation.Cancel();
        return node;
      }, cancellationToken: cancellation.Token));
    Assert.Equal(before, input.ToArray());
    Assert.Equal(0, input.Position);
  }

  [Theory]
  [InlineData("""{"items":[1,2]""")]
  [InlineData("""{"items":[1,2]} garbage""")]
  public async Task MalformedInput_DoesNotOverwriteSource(string json)
  {
    using var input = Input(json);
    var before = input.ToArray();
    await Assert.ThrowsAnyAsync<JsonException>(() => input.SetJsonValueInPlaceAsync("$.items[*]", JsonValue.Create(7)));
    Assert.Equal(before, input.ToArray());
    Assert.Equal(0, input.Position);
  }

  [Fact]
  public async Task CallbackSizeCap_RejectsLargeSelectedValuesButNotStreamingOperations()
  {
    var json = "{\"selected\":{\"large\":[" + string.Join(",", Enumerable.Range(0, 10000)) + "],\"id\":1}}";
    var options = new JsonPathEditingOptions { MaxMaterializedValueBytes = 64 };
    using var source = Input(json);
    var before = source.ToArray();
    await Assert.ThrowsAsync<InvalidOperationException>(() => source.EditJsonInPlaceAsync("$.selected", node => node, options));
    Assert.Equal(before, source.ToArray());
    Assert.Equal(1, await source.ProjectJsonInPlaceAsync("$.selected[id]", options));
    AssertJson("""{"selected":{"id":1}}""", source);
  }

  [Theory]
  [InlineData("$.items[not-an-index]")]
  [InlineData("$.items[")]
  [InlineData("$.items[?()]")]
  [InlineData("$.")]
  [InlineData("$[count(items)]")]
  [InlineData("$.items[id]")]
  public async Task UnsafeOrComputedEditingPaths_AreRejectedBeforeWriting(string path)
  {
    using var input = Input(Source);
    using var output = new MemoryStream();
    await Assert.ThrowsAsync<ArgumentException>(() => input.SetJsonValueAsync(output, path, JsonValue.Create(0)));
    Assert.Equal(0, output.Length);
    Assert.Equal(0, input.Position);
  }

  [Fact]
  public async Task InvalidStreamCombinationsAndRootRemoval_AreRejected()
  {
    using var input = Input(Source);
    await Assert.ThrowsAsync<ArgumentException>(() => input.SetJsonValueAsync(input, "$.nil", JsonValue.Create(0)));
    using var output = new MemoryStream();
    await Assert.ThrowsAsync<ArgumentException>(() => input.RemoveJsonMatchesAsync(output, "$"));
    await Assert.ThrowsAsync<ArgumentException>(() => input.ProjectJsonAsync(output, "$.items"));
    using var nonseekable = new AsyncReadStream(Source, 7);
    await Assert.ThrowsAsync<ArgumentException>(() => nonseekable.ProjectJsonInPlaceAsync("$.items[id]"));
    Assert.Equal(0, output.Length);
  }

  [Fact]
  public async Task AsyncOnlyDestination_IsSupportedAndRemainsOpen()
  {
    using var input = new AsyncReadStream(Source, 7);
    using var output = new AsyncWriteStream();
    Assert.Equal(1, await input.SetJsonValueAsync(output, "$.meta.total", JsonValue.Create(4)));
    Assert.Equal(4, JsonNode.Parse(output.Bytes)!["meta"]!["total"]!.GetValue<int>());
    Assert.True(output.CanWrite);
  }

  [Fact]
  public async Task FixedCapacityStream_GrowthFailsBeforeOverwriting()
  {
    var bytes = Encoding.UTF8.GetBytes("""{"id":1}""");
    using var source = new MemoryStream(bytes);
    await Assert.ThrowsAsync<NotSupportedException>(() =>
      source.SetJsonValueInPlaceAsync("$.id", JsonValue.Create(new string('x', 10000))));
    Assert.Equal("""{"id":1}""", Encoding.UTF8.GetString(bytes));
    Assert.Equal(0, source.Position);
  }

  [Fact]
  public async Task FileStream_ProjectionWritesBackAndTruncatesOnDisk()
  {
    var path = Path.Combine(Path.GetTempPath(), "jsonpathplus-edit-test-" + Guid.NewGuid().ToString("N"));
    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
      4096, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
    {
      await file.WriteAsync(Encoding.UTF8.GetBytes(Source));
      var oldLength = file.Length;
      file.Position = 0;
      Assert.Equal(1, await file.ProjectJsonInPlaceAsync("$.items[id]"));
      Assert.Equal(0, file.Position);
      Assert.True(file.Length < oldLength);
      var result = await JsonNode.ParseAsync(file);
      Assert.Equal(3, result!["items"]!.AsArray().Count);
      Assert.Single(result["items"]![0]!.AsObject());
      Assert.True(file.CanWrite);
    }
    Assert.False(File.Exists(path));
  }

  [Fact]
  public async Task PropertyUnionAndSpecialNames_AreEditedSafely()
  {
    using var input = Input("""{"first":1,"second":2,"quote'key":3,"complex name":4}""");
    using var output = new MemoryStream();
    Assert.Equal(2, await input.SetJsonValueAsync(output, "$['second','first','first']", JsonValue.Create(9)));
    AssertJson("""{"first":9,"second":9,"quote'key":3,"complex name":4}""", output);
    using var secondOutput = new MemoryStream();
    output.Position = 0;
    Assert.Equal(1, await output.EditJsonAsync(secondOutput, "$.complex name", node => JsonValue.Create(node!.GetValue<int>() + 1)));
    Assert.Equal(5, Result(secondOutput)!["complex name"]!.GetValue<int>());
  }

  private static MemoryStream Input(string json)
  {
    var stream = new MemoryStream();
    stream.Write(Encoding.UTF8.GetBytes(json));
    stream.Position = 0;
    return stream;
  }

  private static JsonNode? Result(MemoryStream stream) => JsonNode.Parse(stream.ToArray());
  private static void AssertJson(string expected, MemoryStream actual)
    => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), Result(actual)),
      "Actual JSON: " + Encoding.UTF8.GetString(actual.ToArray()));

  private sealed class AsyncReadStream(string json, int chunkSize) : Stream
  {
    private readonly MemoryStream _inner = new(Encoding.UTF8.GetBytes(json));
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      => _inner.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous reads.");
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }

  }

  private sealed class AsyncWriteStream : Stream
    {
      private readonly MemoryStream _inner = new();
      public byte[] Bytes => _inner.ToArray();
      public override bool CanRead => false;
      public override bool CanSeek => false;
      public override bool CanWrite => _inner.CanWrite;
      public override long Length => throw new NotSupportedException();
      public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
      public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);
      public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
      public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      public override void Flush() => throw new NotSupportedException("Use asynchronous flush.");
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous writes.");
      protected override void Dispose(bool disposing)
      {
        if (disposing)
          _inner.Dispose();
        base.Dispose(disposing);
      }
  }
}
