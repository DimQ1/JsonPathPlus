using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using JsonPathPlus;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class JsonPathStreamingMatcherInternalTests
{
  private const string RootArrayJson = """
  [
    { "id": 1, "name": "one" },
    { "id": 2, "name": "two" },
    { "id": 3, "name": "three" }
  ]
  """;

  private const string RootObjectJson = """
  {
    "simple": { "name": "alpha" },
    "complex name": { "name": "beta" },
    "quote'prop": { "name": "gamma" },
    "books": [
      { "title": "b1", "price": 1 },
      { "title": "b2", "price": 2 }
    ]
  }
  """;

  [Fact]
  public void CanUseStreaming_ReturnsNullForEmptySegmentsAndUnsupportedArraySelectors()
  {
    using var arrayStream = CreateSeekableStream(RootArrayJson);

    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(arrayStream, []));
    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(arrayStream, Segments(ArrayIndex(-1))));
    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(arrayStream, Segments(ArrayRange(-1, 2))));
    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(arrayStream, Segments(ArrayUnion(0, -1))));
  }

  [Fact]
  public void CanUseStreaming_DetectsSupportedSeekableArrayAndObjectRoots()
  {
    using var arrayStream = CreateSeekableStream(RootArrayJson);
    using var objectStream = CreateSeekableStream(RootObjectJson);

    var arrayHead = JsonPathStreamingMatcher.CanUseStreaming(arrayStream, Segments(Wildcard()));
    Assert.True(arrayHead.HasValue);
    Assert.Equal(JsonPathStreamingMatcher.RootContainerKind.Array, arrayHead.Value.RootKind);
    Assert.Equal(0, arrayStream.Position);

    var filterHead = JsonPathStreamingMatcher.CanUseStreaming(CreateSeekableStream(RootArrayJson), Segments(Filter("@.id >= 2")));
    Assert.True(filterHead.HasValue);
    Assert.Equal(JsonPathStreamingMatcher.RootContainerKind.Array, filterHead.Value.RootKind);

    var objectHead = JsonPathStreamingMatcher.CanUseStreaming(objectStream, Segments(PropertyUnion("simple", "complex name")));
    Assert.True(objectHead.HasValue);
    Assert.Equal(JsonPathStreamingMatcher.RootContainerKind.Object, objectHead.Value.RootKind);
    Assert.Equal(0, objectStream.Position);

    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(CreateSeekableStream(RootObjectJson), Segments(Property(" "))));
  }

  [Fact]
  public void CanUseStreaming_NonSeekableStream_CapturesHeadBytesAndSkipsBom()
  {
    var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x20, 0x0A, 0x5B, 0x5D };
    using var stream = new NonSeekableStream(bytes);

    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, Segments(Wildcard()));

    Assert.True(head.HasValue);
    Assert.Equal(JsonPathStreamingMatcher.RootContainerKind.Array, head.Value.RootKind);
    Assert.NotNull(head.Value.HeadBytes);
    Assert.False(head.Value.IsEmpty);
    Assert.True(head.Value.HeadBytes!.Length >= bytes.Length);

    using var emptyStream = new NonSeekableStream([]);
    Assert.Null(JsonPathStreamingMatcher.CanUseStreaming(emptyStream, Segments(Wildcard())));
  }

  [Fact]
  public async Task ExtractFirstMatchAsync_SeekableArrayRoot_RestoresStreamPosition()
  {
    using var stream = CreateSeekableStream(RootArrayJson);
    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, Segments(ArrayIndex(1), Property("name")));

    Assert.True(head.HasValue);
    var result = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(stream, Segments(ArrayIndex(1), Property("name")), head.Value);

    Assert.Equal(0, stream.Position);
    AssertNodeEquals(JsonValue.Create("two"), result);
  }

  [Fact]
  public async Task ExtractFirstMatchAsync_NonSeekableArrayRoot_UsesHeadPrependedStream()
  {
    using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(RootArrayJson));
    var segments = Segments(ArrayIndex(2), Property("id"));
    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, segments);

    Assert.True(head.HasValue);
    var result = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(stream, segments, head.Value);

    AssertNodeEquals(JsonValue.Create(3), result);
  }

  [Fact]
  public async Task ExtractAllMatchesAsync_ArrayRoot_CoversElementStreamingAndFilterStreaming()
  {
    using var rangeStream = CreateSeekableStream(RootArrayJson);
    var rangeSegments = Segments(ArrayRange(1, 3), Property("name"));
    var rangeHead = JsonPathStreamingMatcher.CanUseStreaming(rangeStream, rangeSegments);

    var rangeMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(rangeStream, rangeSegments, rangeHead!.Value));
    Assert.Equal(2, rangeMatches.Count);
    AssertNodeEquals(JsonValue.Create("two"), rangeMatches[0]);
    AssertNodeEquals(JsonValue.Create("three"), rangeMatches[1]);

    using var filterStream = CreateSeekableStream(RootArrayJson);
    var filterSegments = Segments(Filter("@.id >= 2"), Property("id"));
    var filterHead = JsonPathStreamingMatcher.CanUseStreaming(filterStream, filterSegments);

    var filterMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(filterStream, filterSegments, filterHead!.Value));
    Assert.Equal(2, filterMatches.Count);
    AssertNodeEquals(JsonValue.Create(2), filterMatches[0]);
    AssertNodeEquals(JsonValue.Create(3), filterMatches[1]);
  }

  [Fact]
  public async Task ExtractAllMatchesWithPathsAsync_ArrayRoot_ReturnsElementPaths()
  {
    using var stream = CreateSeekableStream(RootArrayJson);
    var segments = Segments(Wildcard(), Property("name"));
    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, segments);

    var matches = await CollectPathMatchesAsync(JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(stream, segments, head!.Value));
    Assert.Equal(3, matches.Count);
    Assert.Equal("$[0].name", matches[0].Path);
    Assert.Equal("$[1].name", matches[1].Path);
    Assert.Equal("$[2].name", matches[2].Path);
  }

  [Fact]
  public async Task ExtractArrayRootStreaming_SingleSegmentAndNoMatchCases_ReturnExpected()
  {
    using var singleWildcardStream = CreateSeekableStream(RootArrayJson);
    var singleWildcardSegments = Segments(Wildcard());
    var singleWildcardHead = JsonPathStreamingMatcher.CanUseStreaming(singleWildcardStream, singleWildcardSegments);

    var firstItem = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(singleWildcardStream, singleWildcardSegments, singleWildcardHead!.Value);
    Assert.IsType<JsonObject>(firstItem);

    using var singleIndexStream = CreateSeekableStream(RootArrayJson);
    var singleIndexSegments = Segments(ArrayIndex(2));
    var singleIndexHead = JsonPathStreamingMatcher.CanUseStreaming(singleIndexStream, singleIndexSegments);

    var indexedMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(singleIndexStream, singleIndexSegments, singleIndexHead!.Value));
    Assert.Single(indexedMatches);
    Assert.IsType<JsonObject>(indexedMatches[0]);

    using var singleRangeStream = CreateSeekableStream(RootArrayJson);
    var singleRangeSegments = Segments(ArrayRange(1, 3));
    var singleRangeHead = JsonPathStreamingMatcher.CanUseStreaming(singleRangeStream, singleRangeSegments);

    var rangePaths = await CollectPathMatchesAsync(JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(singleRangeStream, singleRangeSegments, singleRangeHead!.Value));
    Assert.Equal(2, rangePaths.Count);
    Assert.Equal("$[1]", rangePaths[0].Path);
    Assert.Equal("$[2]", rangePaths[1].Path);

    using var noMatchFilterStream = CreateSeekableStream(RootArrayJson);
    var noMatchSegments = Segments(Filter("@.id > 99"), Property("name"));
    var noMatchHead = JsonPathStreamingMatcher.CanUseStreaming(noMatchFilterStream, noMatchSegments);

    var noMatch = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(noMatchFilterStream, noMatchSegments, noMatchHead!.Value);
    Assert.Null(noMatch);
  }

  [Fact]
  public async Task ExtractAllMatchesAsync_ObjectRoot_CoversWildcardAndPropertyUnion()
  {
    using var wildcardStream = CreateSeekableStream(RootObjectJson);
    var wildcardSegments = Segments(Wildcard(), Property("name"));
    var wildcardHead = JsonPathStreamingMatcher.CanUseStreaming(wildcardStream, wildcardSegments);

    var wildcardMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(wildcardStream, wildcardSegments, wildcardHead!.Value));
    Assert.Equal(3, wildcardMatches.Count);

    using var unionStream = CreateSeekableStream(RootObjectJson);
    var unionSegments = Segments(PropertyUnion("simple", "complex name", "quote'prop"), Property("name"));
    var unionHead = JsonPathStreamingMatcher.CanUseStreaming(unionStream, unionSegments);

    var unionMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(unionStream, unionSegments, unionHead!.Value));
    Assert.Equal(3, unionMatches.Count);
    AssertNodeEquals(JsonValue.Create("alpha"), unionMatches[0]);
    AssertNodeEquals(JsonValue.Create("beta"), unionMatches[1]);
    AssertNodeEquals(JsonValue.Create("gamma"), unionMatches[2]);
  }

  [Fact]
  public async Task ExtractAllMatchesWithPathsAsync_ObjectRoot_CoversEscapingAndFallback()
  {
    using var unionStream = CreateSeekableStream(RootObjectJson);
    var unionSegments = Segments(PropertyUnion("simple", "complex name", "quote'prop"), Property("name"));
    var unionHead = JsonPathStreamingMatcher.CanUseStreaming(unionStream, unionSegments);

    var unionMatches = await CollectPathMatchesAsync(JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(unionStream, unionSegments, unionHead!.Value));
    Assert.Equal(3, unionMatches.Count);
    Assert.Equal("$.simple.name", unionMatches[0].Path);
    Assert.Equal("$['complex name'].name", unionMatches[1].Path);
    Assert.Equal("$['quote\\'prop'].name", unionMatches[2].Path);

    using var fallbackStream = CreateSeekableStream(RootObjectJson);
    var fallbackSegments = Segments(Property("books"), FieldProjection("title"));
    var fallbackHead = JsonPathStreamingMatcher.CanUseStreaming(fallbackStream, fallbackSegments);

    var fallbackMatches = await CollectPathMatchesAsync(JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(fallbackStream, fallbackSegments, fallbackHead!.Value));
    Assert.Equal(2, fallbackMatches.Count);
    Assert.Equal("$.books[0]", fallbackMatches[0].Path);
    Assert.Equal("$.books[1]", fallbackMatches[1].Path);
  }

  [Fact]
  public async Task ExtractObjectRootStreaming_SinglePropertyAndNoMatchCases_ReturnExpected()
  {
    using var singlePropertyStream = CreateSeekableStream(RootObjectJson);
    var singlePropertySegments = Segments(Property("simple"));
    var singlePropertyHead = JsonPathStreamingMatcher.CanUseStreaming(singlePropertyStream, singlePropertySegments);

    var firstMatch = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(singlePropertyStream, singlePropertySegments, singlePropertyHead!.Value);
    Assert.IsType<JsonObject>(firstMatch);

    using var singlePropertyPathStream = CreateSeekableStream(RootObjectJson);
    var pathHead = JsonPathStreamingMatcher.CanUseStreaming(singlePropertyPathStream, singlePropertySegments);
    var pathMatches = await CollectPathMatchesAsync(JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(singlePropertyPathStream, singlePropertySegments, pathHead!.Value));
    Assert.Single(pathMatches);
    Assert.Equal("$.simple", pathMatches[0].Path);

    using var missingPropertyStream = CreateSeekableStream(RootObjectJson);
    var missingSegments = Segments(Property("missing"));
    var missingHead = JsonPathStreamingMatcher.CanUseStreaming(missingPropertyStream, missingSegments);
    Assert.True(missingHead.HasValue);

    var missingMatches = await CollectAsync(JsonPathStreamingMatcher.ExtractAllMatchesAsync(missingPropertyStream, missingSegments, missingHead.Value));
    Assert.Empty(missingMatches);
  }

  [Fact]
  public async Task PrivateReadToEndAsync_CoversSeekableAndNonSeekablePaths()
  {
    var method = typeof(JsonPathStreamingMatcher)
      .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
      .Single(m => m.Name == "ReadToEndAsync" && m.GetParameters().Length == 2);

    using var seekableStream = CreateSeekableStream("[1,2,3]");
    var seekableResult = await InvokeReadToEndAsync(method, seekableStream, null);
    Assert.Equal("[1,2,3]", Encoding.UTF8.GetString(seekableResult));

    using var nonSeekableStream = new NonSeekableStream(Encoding.UTF8.GetBytes("{\"id\":1}"));
    var headedResult = await InvokeReadToEndAsync(method, nonSeekableStream, Encoding.UTF8.GetBytes(" "));
    Assert.Equal(" {\"id\":1}", Encoding.UTF8.GetString(headedResult));
  }

  [Fact]
  public void AppendPropertyPath_UsesBracketPathsForSpecialIdentifiers()
  {
    var method = typeof(JsonPathStreamingMatcher)
      .GetMethod("AppendPropertyPath", BindingFlags.NonPublic | BindingFlags.Static)!;

    Assert.Equal("$['']", (string)method.Invoke(null, ["$", ""])!);
    Assert.Equal("$['1name']", (string)method.Invoke(null, ["$", "1name"])!);
  }

  [Fact]
  public async Task ExtractFirstMatchAsync_NoneRootKind_ReturnsNull()
  {
    using var stream = CreateSeekableStream("123");
    var result = await JsonPathStreamingMatcher.ExtractFirstMatchAsync(
      stream,
      Segments(Wildcard()),
      new JsonPathStreamingMatcher.StreamHead { RootKind = JsonPathStreamingMatcher.RootContainerKind.None });

    Assert.Null(result);
  }

  private static MemoryStream CreateSeekableStream(string json)
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

  private static async Task<byte[]> InvokeReadToEndAsync(MethodInfo method, Stream stream, byte[]? head)
    => await (Task<byte[]>)method.Invoke(null, [stream, head])!;

  private static List<JsonPathSegment> Segments(params JsonPathSegment[] segments)
    => new(segments);

  private static JsonPathSegment Property(string name)
    => new(name, -1, -1, JsonPathSegmentType.Property);

  private static JsonPathSegment ArrayIndex(int index)
    => new(null, index, -1, JsonPathSegmentType.ArrayIndex);

  private static JsonPathSegment ArrayRange(int start, int end)
    => new(null, start, end, JsonPathSegmentType.ArrayRange);

  private static JsonPathSegment ArrayUnion(params int[] indices)
    => new(null, -1, -1, JsonPathSegmentType.ArrayUnion, indices);

  private static JsonPathSegment Filter(string expression)
    => new(null, -1, -1, JsonPathSegmentType.Filter, null, null, expression);

  private static JsonPathSegment Wildcard()
    => new(null, -1, -1, JsonPathSegmentType.Wildcard);

  private static JsonPathSegment PropertyUnion(params string[] properties)
    => new(null, -1, -1, JsonPathSegmentType.PropertyUnion, null, properties);

  private static JsonPathSegment FieldProjection(params string[] fields)
    => new(null, -1, -1, JsonPathSegmentType.FieldProjection, null, null, null, null, fields);

  private static void AssertNodeEquals(JsonNode? expected, JsonNode? actual)
  {
    Assert.True(JsonNode.DeepEquals(expected, actual),
      $"Expected: {expected?.ToJsonString() ?? "null"}, Actual: {actual?.ToJsonString() ?? "null"}");
  }

  private sealed class NonSeekableStream : MemoryStream
  {
    public NonSeekableStream(byte[] buffer)
      : base(buffer)
    {
    }

    public override bool CanSeek => false;

    public override long Seek(long offset, SeekOrigin loc)
      => throw new NotSupportedException();
  }
}