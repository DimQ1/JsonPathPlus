using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class JsonPathMemoryStreamingTests
{
  [Fact]
  public void DeferredLocation_PreservesEscapingAndIndexFormatting()
  {
    var property = new JsonPathLocation(JsonPathLocation.Root, "a\\b'c");
    Assert.Equal("$['a\\\\b\\'c']", property.ToString());
    Assert.Equal("$['a\\\\b\\'c'][12345].name",
      new JsonPathLocation(new JsonPathLocation(property, 12345), "name").ToString());
    Assert.Equal("$[-2147483648]", new JsonPathLocation(JsonPathLocation.Root, int.MinValue).ToString());
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ByteCaps_ApplyToTokensOrCapturedValuesNotTheEntireStreamingArray(bool tokenCap)
  {
    var json = "{\"items\":[" + string.Join(",", Enumerable.Range(0, 3000)
      .Select(i => $"{{\"id\":{i},\"payload\":\"tiny\"}}")) + "]}";
    using var input = new CountingStream(Encoding.UTF8.GetBytes(json), false, 127, asyncOnly: true);
    var options = tokenCap
      ? new JsonPathExtractionOptions { MaxTokenBytes = 32 }
      : new JsonPathExtractionOptions { MaxCapturedValueBytes = 16 };
    var expected = 0;
    await foreach (var result in input.ExtractAllJsonMatchesAsync("$.items[*].id", options))
      Assert.Equal(expected++, result!.GetValue<int>());
    Assert.Equal(3000, expected);
  }

  [Fact]
  public async Task ExplicitMaxDepth_IsHonoredByStreamAndFallback()
  {
    var json = string.Concat(Enumerable.Repeat("{\"child\":", 80)) + "{\"id\":7}" + new string('}', 80);
    var path = "$" + string.Concat(Enumerable.Repeat(".child", 80)) + ".id";
    foreach (var query in new[] { path, "$" })
    {
      using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
      var result = await input.ExtractFirstJsonMatchAsync(query, new JsonPathExtractionOptions { MaxDepth = 128 });
      Assert.NotNull(result);
    }
    using var defaultInput = new MemoryStream(Encoding.UTF8.GetBytes(json));
    await Assert.ThrowsAnyAsync<JsonException>(() => defaultInput.ExtractFirstJsonMatchAsync(path));
  }

  [Theory]
  [InlineData("$.items[*].id", true)]
  [InlineData("$", true)]
  [InlineData("$.items[?(@.id == 1)].id", false)]
  [InlineData("$", false)]
  public async Task ExplicitByteLimits_RejectOversizedTokensAndCapturedValues(string path, bool tokenLimit)
  {
    var json = "{\"items\":[{\"id\":1,\"payload\":\"" + new string('x', 100000) + "\"}]}";
    using var input = new CountingStream(Encoding.UTF8.GetBytes(json), false, 127);
    var options = tokenLimit
      ? new JsonPathExtractionOptions { MaxTokenBytes = 1024 }
      : new JsonPathExtractionOptions { MaxCapturedValueBytes = 1024 };
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
      await foreach (var _ in input.ExtractAllJsonMatchesAsync(path, options)) { }
    });
    Assert.True(input.CanRead);
  }

  [Theory]
  [InlineData("$.groups[?(@.id == 0)].items[?(@.id > 1997)].id")]
  [InlineData("$.groups[?(@.present && @.id == 0)].items[-1].id")]
  [InlineData("$.groups[?(!@.missing && @.id == 0)].items[(@.length-1)].id")]
  [InlineData("$.groups[*].items[-3:-1].id")]
  [InlineData("$.groups[*].items[1999,0,1999,-2].id")]
  [InlineData("$.groups[*].items..id")]
  [InlineData("$.groups[*].items[id].id")]
  [InlineData("$.groups[*].items[!name].id")]
  [InlineData("$.groups[*][items[-1].id, id]")]
  [InlineData("$..id")]
  [InlineData("$..[0].id")]
  [InlineData("$..[-1].id")]
  public async Task LargeStreamPlans_PreserveDomResultsAndPaths(string path)
  {
    var items = string.Join(",", Enumerable.Range(0, 2000).Select(i => $"{{\"id\":{i},\"name\":\"item\"}}"));
    var json = "{\"groups\":[{\"id\":0,\"present\":[],\"items\":[" + items + "]}]}";
    var expected = new List<string>();
    await foreach (var match in JsonNode.Parse(json).ExtractAllJsonMatchesWithPathsAsync(path))
      expected.Add(match.Path + ":" + match.Value?.ToJsonString());

    foreach (var seekable in new[] { false, true })
    {
      using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), seekable, 127, asyncOnly: true);
      var actual = new List<string>();
      await foreach (var match in stream.ExtractAllJsonMatchesWithPathsAsync(path))
        actual.Add(match.Path + ":" + match.Value?.ToJsonString());
      Assert.Equal(expected, actual);
      Assert.True(stream.CanRead);
      if (seekable)
        Assert.Equal(0, stream.Position);
    }
  }

  [Fact]
  public async Task CapturedLargeValue_SpillsToFileAndDeletesOnDisposal()
  {
    var json = "{\"payload\":\"" + new string('x', 100000) + "\"}";
    using var input = new CountingStream(Encoding.UTF8.GetBytes(json), false, 4096, asyncOnly: true);
    using var reader = new JsonPathStreamReader(input, default);
    Assert.True(await reader.ReadAsync());
    string fileName;
    using (var value = await reader.CaptureValueAsync())
    {
      var file = Assert.IsType<FileStream>(value.Stream);
      fileName = file.Name;
      Assert.True(File.Exists(fileName));
      using var document = await JsonDocument.ParseAsync(value.Stream);
      Assert.Equal(100000, document.RootElement.GetProperty("payload").GetString()!.Length);
    }
    Assert.False(File.Exists(fileName));
    Assert.True(input.CanRead);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task FirstMatch_DoesNotReadEntireArray(bool objectRoot, bool seekable)
  {
    var json = LargeArray(objectRoot);
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), seekable);
    var path = objectRoot ? "$.data.items[?(@.id == 0)].name" : "$[?(@.id == 0)].name";

    var result = await stream.ExtractFirstJsonMatchAsync(path);

    Assert.Equal("first", result!.GetValue<string>());
    Assert.InRange(stream.BytesRead, 1, 32768);
    Assert.True(stream.BytesRead < json.Length / 2);
    Assert.True(stream.CanRead);
    if (seekable)
      Assert.Equal(0, stream.Position);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task AllMatches_FirstMoveNextIsLazy_AndDisposalPreservesInput(bool objectRoot, bool withPaths)
  {
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(LargeArray(objectRoot)), true);
    var path = objectRoot ? "$.data.items[?(@.id >= 0)].name" : "$[?(@.id >= 0)].name";
    if (withPaths)
    {
      await using var matches = stream.ExtractAllJsonMatchesWithPathsAsync(path).GetAsyncEnumerator();
      Assert.True(await matches.MoveNextAsync());
      Assert.Equal(objectRoot ? "$.data.items[0].name" : "$[0].name", matches.Current.Path);
      Assert.Equal("first", matches.Current.Value!.GetValue<string>());
    }
    else
    {
      await using var matches = stream.ExtractAllJsonMatchesAsync(path).GetAsyncEnumerator();
      Assert.True(await matches.MoveNextAsync());
      Assert.Equal("first", matches.Current!.GetValue<string>());
    }
    Assert.InRange(stream.BytesRead, 1, 32768);
    Assert.Equal(0, stream.Position);
    Assert.True(stream.CanRead);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(7)]
  [InlineData(16384)]
  public async Task ShortReads_BomEscapesAndLargeElements_AreHandled(int chunkSize)
  {
    var large = new string('x', 40000);
    var json = "{\"ignored\":{\"payload\":\"" + large + "\"},\"complex name\":{\"items\":["
      + "{\"id\":1,\"name\":\"" + large + "\"},"
      + "{\"id\":2,\"name\":\"\\u043F\\u0440\\u0438\\u0432\\u0435\\u0442\"}]}}";
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
    using var stream = new CountingStream(bytes, false, chunkSize);
    var results = new List<JsonPathMatch>();
    await foreach (var match in stream.ExtractAllJsonMatchesWithPathsAsync("$.complex name.items[?(@.id >= 1)].name"))
      results.Add(match);

    Assert.Equal(2, results.Count);
    Assert.Equal(large, results[0].Value!.GetValue<string>());
    Assert.Equal("\u043F\u0440\u0438\u0432\u0435\u0442", results[1].Value!.GetValue<string>());
    Assert.Equal("$['complex name'].items[1].name", results[1].Path);
  }

  [Theory]
  [InlineData("$[-1].id", 3)]
  [InlineData("$..id", 1)]
  public async Task NonSeekableFallback_ReplaysConsumedPrefix(string path, int expected)
  {
    using var stream = new CountingStream(Encoding.UTF8.GetBytes("[{\"id\":1},{\"id\":3}]"), false, 1);
    var result = await stream.ExtractFirstJsonMatchAsync(path);
    Assert.Equal(expected, result!.GetValue<int>());
    Assert.True(stream.CanRead);
  }

  [Fact]
  public async Task AsyncOnlyShortReadStream_DoesNotUseSynchronousReads()
  {
    using var stream = new CountingStream(
      Encoding.UTF8.GetBytes("""{"items":[{"id":1},{"id":2},{"id":3}]}"""),
      false, 2, asyncOnly: true);
    var results = new List<int>();
    await foreach (var match in stream.ExtractAllJsonMatchesAsync("$.items[?(@.id > 1)].id"))
      results.Add(match!.GetValue<int>());
    Assert.Equal(new[] { 2, 3 }, results);
    Assert.True(stream.CanRead);
  }

  [Fact]
  public async Task LargeCandidateWithManyTokens_CanSpanManyAsynchronousReads()
  {
    var numbers = string.Join(",", Enumerable.Range(0, 10000));
    var json = "{\"items\":[{\"id\":1,\"numbers\":[" + numbers + "]},{\"id\":2}]}";
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), false, 127, asyncOnly: true);
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[?(@.id == 2)].id");
    Assert.Equal(2, result!.GetValue<int>());
  }

  [Fact]
  public async Task SeekableStream_RestoresNonZeroStartingPosition()
  {
    using var stream = new CountingStream(Encoding.UTF8.GetBytes("""prefix[{"id":7}]"""), true);
    stream.Position = 6;
    var result = await stream.ExtractFirstJsonMatchAsync("$[?(@.id == 7)].id");
    Assert.Equal(7, result!.GetValue<int>());
    Assert.Equal(6, stream.Position);
  }

  [Theory]
  [InlineData("@.score >= 2 && @.active == true")]
  [InlineData("@.nested.score == '2' || @.score < 0")]
  [InlineData("!@.missing")]
  [InlineData("@.empty == null")]
  [InlineData("@.empty != null")]
  [InlineData("@.score == @.nested.score")]
  [InlineData("@.name >= 'abc'")]
  [InlineData("@.nested == @.nested")]
  [InlineData("@.missing != 0")]
  public void CompiledFilter_JsonElementAndJsonNodeHaveSameSemantics(string expression)
  {
    const string json = """{"score":2,"active":true,"nested":{"score":2},"empty":null,"name":"abcd"}""";
    var node = JsonNode.Parse(json);
    using var document = JsonDocument.Parse(json);
    var filter = JsonPathFilterNode.Compile(expression);

    Assert.Equal(filter.Evaluate(node), filter.Evaluate(document.RootElement));
  }

  [Theory]
  [InlineData("null")]
  [InlineData("true")]
  [InlineData("false")]
  [InlineData("2")]
  [InlineData("\"2\"")]
  [InlineData("{}")]
  [InlineData("[]")]
  public void CurrentValueFilter_JsonElementAndJsonNodeHaveSameSemantics(string json)
  {
    var node = JsonNode.Parse(json);
    using var document = JsonDocument.Parse(json);
    foreach (var expression in new[] { "@", "@ == null", "@ != null", "@ > 1", "@ == true", "@ == @" })
    {
      var filter = JsonPathFilterNode.Compile(expression);
      Assert.Equal(filter.Evaluate(node), filter.Evaluate(document.RootElement));
    }
  }

  [Fact]
  public void CompiledNumericAndBooleanFilter_DoesNotBoxPerCandidate()
  {
    var node = new JsonObject { ["score"] = 42, ["active"] = true };
    using var document = JsonDocument.Parse("""{"score":42,"active":true}""");
    var filter = JsonPathFilterNode.Compile("@.score >= 2 && @.active == true");
    for (var i = 0; i < 10000; i++)
    {
      Assert.True(filter.Evaluate(node));
      Assert.True(filter.Evaluate(document.RootElement));
    }

    var before = GC.GetAllocatedBytesForCurrentThread();
    var count = 0;
    for (var i = 0; i < 10000; i++)
    {
      if (filter.Evaluate(node))
        count++;
      if (filter.Evaluate(document.RootElement))
        count++;
    }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.Equal(20000, count);
    Assert.InRange(allocated, 0, 1024);
  }

  [Theory]
  [InlineData("@.payload")]
  [InlineData("@.payload == 'different'")]
  [InlineData("@.payload != 'different'")]
  public void StringPresenceAndLiteralComparison_DoNotDecodeLargeStrings(string expression)
  {
    using var document = JsonDocument.Parse("{\"payload\":\"" + new string('x', 100000) + "\"}");
    var filter = JsonPathFilterNode.Compile(expression);
    _ = filter.Evaluate(document.RootElement);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++)
      _ = filter.Evaluate(document.RootElement);
    Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 1024);
  }

  [Theory]
  [InlineData("123")]
  [InlineData("1e3")]
  [InlineData("1234567890123456789012345678")]
  [InlineData("\"hello\"")]
  [InlineData("true")]
  [InlineData("null")]
  public void MaterializedScalars_PreserveJsonAndSurviveDocumentDisposal(string json)
  {
    JsonNode? result;
    using (var document = JsonDocument.Parse(json))
      result = JsonPathElementMatcher.Materialize(document.RootElement);
    Assert.Equal(json, result?.ToJsonString() ?? "null");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NodeMatching_DoesNotEvaluateLaterCandidatesBeforeYield(bool withPaths)
  {
    var node = new JsonArray(
      new JsonObject { ["score"] = 1 },
      new JsonObject { ["score"] = double.PositiveInfinity });
    const string path = "$[?(@.score >= 0)].score";

    if (withPaths)
    {
      await using var matches = node.ExtractAllJsonMatchesWithPathsAsync(path).GetAsyncEnumerator();
      Assert.True(await matches.MoveNextAsync());
      Assert.Equal("$[0].score", matches.Current.Path);
      await Assert.ThrowsAsync<OverflowException>(async () => await matches.MoveNextAsync());
    }
    else
    {
      await using var matches = node.ExtractAllJsonMatchesAsync(path).GetAsyncEnumerator();
      Assert.True(await matches.MoveNextAsync());
      Assert.Equal(1, matches.Current!.GetValue<int>());
      await Assert.ThrowsAsync<OverflowException>(async () => await matches.MoveNextAsync());
    }
  }

  [Theory]
  [InlineData("$.items[2,0,2].id", "3,1,3")]
  [InlineData("$.items[-1].id", "3")]
  [InlineData("$.items[1:].id", "2,3")]
  [InlineData("$.items[?(@.id >= 2)].id", "2,3")]
  [InlineData("$.items[?(@.id >= 2)][id]", "{\"id\":2},{\"id\":3}")]
  [InlineData("$.items[*].missing", "")]
  public async Task NestedArraySelectors_PreserveOrderAndFallback(string path, string expected)
  {
    const string json = """{"items":[{"id":1},{"id":2},{"id":3}]}""";
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), false, 3);
    var results = new List<string>();
    await foreach (var match in stream.ExtractAllJsonMatchesAsync(path))
      results.Add(match!.ToJsonString());
    Assert.Equal(expected, string.Join(",", results));
  }

  [Fact]
  public async Task Cancellation_AfterFirstMatchStopsBufferedTraversalAndRestoresPosition()
  {
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(LargeArray(true)), true);
    using var cancellation = new CancellationTokenSource();
    var matches = stream.ExtractAllJsonMatchesAsync("$.data.items[?(@.id >= 0)]", cancellation.Token).GetAsyncEnumerator();
    Assert.True(await matches.MoveNextAsync());
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await matches.MoveNextAsync());
    await matches.DisposeAsync();
    Assert.Equal(0, stream.Position);
    Assert.True(stream.CanRead);
  }

  [Theory]
  [InlineData("""{"items":[{"id":1}""")]
  [InlineData("""{"items":[{"id":1},invalid]}""")]
  [InlineData("""{"items":[{"id":1,"value":"unfinished""")]
  public async Task InvalidJson_ThrowsDuringEnumeration(string json)
  {
    using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), false, 2);
    await Assert.ThrowsAnyAsync<JsonException>(async () =>
    {
      await foreach (var _ in stream.ExtractAllJsonMatchesAsync("$.items[?(@.id >= 0)]"))
      {
      }
    });
  }

  private static string LargeArray(bool objectRoot)
  {
    var json = new StringBuilder();
    if (objectRoot)
      json.Append("{\"data\":{\"items\":");
    json.Append("[{\"id\":0,\"name\":\"first\"}");
    for (var i = 1; i < 20000; i++)
      json.Append(",{\"id\":").Append(i).Append(",\"name\":\"later\"}");
    json.Append(']');
    if (objectRoot)
      json.Append("}}");
    return json.ToString();
  }

  private sealed class CountingStream(
    byte[] bytes, bool seekable, int chunkSize = int.MaxValue, bool asyncOnly = false) : Stream
  {
    private readonly MemoryStream _inner = new(bytes);
    public long BytesRead { get; private set; }
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => seekable;
    public override bool CanWrite => false;
    public override long Length => seekable ? _inner.Length : throw new NotSupportedException();
    public override long Position
    {
      get => seekable ? _inner.Position : throw new NotSupportedException();
      set
      {
        if (!seekable)
          throw new NotSupportedException();
        _inner.Position = value;
      }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
      if (asyncOnly)
        throw new InvalidOperationException("Synchronous reads are not supported.");
      return ReadCore(buffer);
    }
    private int ReadCore(Span<byte> buffer)
    {
      var count = _inner.Read(buffer[..Math.Min(chunkSize, buffer.Length)]);
      BytesRead += count;
      return count;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (asyncOnly)
        await Task.Yield();
      cancellationToken.ThrowIfCancellationRequested();
      return ReadCore(buffer.Span);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
      => seekable ? _inner.Seek(offset, origin) : throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }
  }
}
