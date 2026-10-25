using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal static class JsonPathStreamingMatcher
{
  internal enum RootContainerKind { None, Array, Object }

  public readonly record struct StreamHead
  {
    public RootContainerKind RootKind { get; init; }
    public byte[]? HeadBytes { get; init; }
    public bool IsEmpty => RootKind == RootContainerKind.None;
  }

  public static StreamHead? CanUseStreaming(Stream stream, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0)
      return null;

    var head = PeekStreamHead(stream);
    return CanStream(head.RootKind, segments[0]) ? head : null;
  }

  internal static bool CanStream(RootContainerKind rootKind, JsonPathSegment first)
    => rootKind switch
    {
      RootContainerKind.Array => CanStreamArray(first),
      RootContainerKind.Object => first.SegmentType switch
      {
        JsonPathSegmentType.Property => !string.IsNullOrWhiteSpace(first.PropertyName),
        JsonPathSegmentType.Wildcard => true,
        JsonPathSegmentType.PropertyUnion => first.PropertyUnionNames is { Length: > 0 },
        _ => false
      },
      _ => false
    };

  private static bool CanStreamArray(JsonPathSegment segment)
    => segment.SegmentType switch
    {
      JsonPathSegmentType.Wildcard => true,
      JsonPathSegmentType.ArrayIndex => segment.ArrayIndex >= 0,
      JsonPathSegmentType.ArrayRange => segment.ArrayIndex >= 0 && segment.ArrayRangeEnd >= 0,
      JsonPathSegmentType.ArrayUnion => AllNonNegative(segment.ArrayUnionIndices),
      JsonPathSegmentType.Filter => !string.IsNullOrWhiteSpace(segment.FilterExpression),
      _ => false
    };

  private static bool AllNonNegative(int[]? indices)
  {
    if (indices is null)
      return false;
    foreach (var index in indices)
    {
      if (index < 0)
        return false;
    }
    return true;
  }

  public static async Task<JsonNode?> ExtractFirstMatchAsync(
    Stream stream, List<JsonPathSegment> segments, StreamHead head, CancellationToken cancellationToken = default)
  {
    await foreach (var match in ExtractAsync(stream, segments, head, false, cancellationToken))
      return match.Value;
    return null;
  }

  public static async IAsyncEnumerable<JsonNode?> ExtractAllMatchesAsync(
    Stream stream, List<JsonPathSegment> segments, StreamHead head,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var match in ExtractAsync(stream, segments, head, false, cancellationToken))
      yield return match.Value;
  }

  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllMatchesWithPathsAsync(
    Stream stream, List<JsonPathSegment> segments, StreamHead head,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var match in ExtractAsync(stream, segments, head, true, cancellationToken))
      yield return new JsonPathMatch(match.Path, match.Value);
  }

  private static async IAsyncEnumerable<JsonPathValueMatch> ExtractAsync(
    Stream stream, List<JsonPathSegment> segments, StreamHead head, bool withPaths,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    if (head.IsEmpty)
      yield break;

    var origin = stream.CanSeek ? stream.Position : -1;
    using var prefixed = head.HeadBytes is { Length: > 0 } ? new PrefixReadStream(head.HeadBytes, stream) : null;
    using var reader = new JsonPathStreamReader(prefixed ?? stream, cancellationToken);
    try
    {
      if (await reader.ReadAsync().ConfigureAwait(false))
      {
        await foreach (var match in TraverseAsync(reader, segments, 0, withPaths ? "$" : null, cancellationToken))
          yield return match;
      }
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  private static async IAsyncEnumerable<JsonPathValueMatch> TraverseAsync(
    JsonPathStreamReader reader, List<JsonPathSegment> segments, int segmentIndex, string? path,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (segmentIndex >= segments.Count)
    {
      using var document = await reader.ReadValueAsync().ConfigureAwait(false);
      yield return new JsonPathValueMatch(path ?? "$", JsonPathElementMatcher.Materialize(document.RootElement));
      yield break;
    }

    var segment = segments[segmentIndex];
    if ((segment.SegmentType is JsonPathSegmentType.Property or JsonPathSegmentType.PropertyUnion
          && reader.TokenType != JsonTokenType.StartObject)
        || (segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ArrayRange
          or JsonPathSegmentType.ArrayUnion or JsonPathSegmentType.ComputedIndex
          && reader.TokenType != JsonTokenType.StartArray)
        || (segment.SegmentType is JsonPathSegmentType.Wildcard or JsonPathSegmentType.Filter
          && reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject)))
    {
      await reader.SkipValueAsync().ConfigureAwait(false);
      yield break;
    }

    if (reader.TokenType == JsonTokenType.StartArray && CanStreamArray(segment)
        && (segmentIndex == 0 || segment.SegmentType != JsonPathSegmentType.ArrayUnion))
    {
      var index = 0;
      var filter = segment.SegmentType == JsonPathSegmentType.Filter
        ? segment.FilterNode ?? JsonPathFilterNode.Compile(segment.FilterExpression!)
        : null;
      while (await reader.ReadAsync().ConfigureAwait(false))
      {
        if (reader.TokenType == JsonTokenType.EndArray)
          yield break;

        var selected = segment.SegmentType switch
        {
          JsonPathSegmentType.ArrayIndex => index == segment.ArrayIndex,
          JsonPathSegmentType.ArrayRange => index >= segment.ArrayIndex && index < segment.ArrayRangeEnd,
          JsonPathSegmentType.ArrayUnion => Array.IndexOf(segment.ArrayUnionIndices!, index) >= 0,
          _ => true
        };

        if (!selected)
        {
          await reader.SkipValueAsync().ConfigureAwait(false);
        }
        else if (filter is not null)
        {
          using var document = await reader.ReadValueAsync().ConfigureAwait(false);
          if (filter.Evaluate(document.RootElement))
          {
            foreach (var match in JsonPathElementMatcher.FindMatches(document.RootElement, segments,
              segmentIndex + 1, path is null ? null : JsonPathElementMatcher.ArrayPath(path, index)))
            {
              cancellationToken.ThrowIfCancellationRequested();
              yield return match;
            }
          }
        }
        else
        {
          await foreach (var match in TraverseAsync(reader, segments, segmentIndex + 1,
            path is null ? null : JsonPathElementMatcher.ArrayPath(path, index), cancellationToken))
            yield return match;
        }

        index++;
      }
      throw new JsonException("Incomplete JSON array.");
    }

    if (reader.TokenType == JsonTokenType.StartObject
        && segment.SegmentType is JsonPathSegmentType.Property or JsonPathSegmentType.PropertyUnion or JsonPathSegmentType.Wildcard
        && (segmentIndex == 0 || segment.SegmentType != JsonPathSegmentType.PropertyUnion))
    {
      while (await reader.ReadAsync().ConfigureAwait(false))
      {
        if (reader.TokenType == JsonTokenType.EndObject)
          yield break;
        if (reader.TokenType != JsonTokenType.PropertyName)
          throw new JsonException("Expected a JSON property.");

        var name = reader.PropertyName!;
        var selected = segment.SegmentType switch
        {
          JsonPathSegmentType.Property => string.Equals(segment.PropertyName, name, StringComparison.Ordinal),
          JsonPathSegmentType.PropertyUnion => segment.PropertyUnionNames is not null
            && Array.IndexOf(segment.PropertyUnionNames, name) >= 0,
          _ => true
        };
        if (!await reader.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing JSON property value.");

        if (selected)
        {
          await foreach (var match in TraverseAsync(reader, segments, segmentIndex + 1,
            path is null ? null : JsonPathElementMatcher.PropertyPath(path, name), cancellationToken))
            yield return match;
        }
        else
        {
          await reader.SkipValueAsync().ConfigureAwait(false);
        }
      }
      throw new JsonException("Incomplete JSON object.");
    }

    using var subtree = await reader.ReadValueAsync().ConfigureAwait(false);
    foreach (var match in JsonPathElementMatcher.FindMatches(subtree.RootElement, segments, segmentIndex, path))
    {
      cancellationToken.ThrowIfCancellationRequested();
      yield return match;
    }
  }

  internal static StreamHead PeekStreamHead(Stream stream)
  {
    var origin = stream.CanSeek ? stream.Position : -1;
    var bytes = new byte[64];
    var length = 0;
    try
    {
      // Short reads may split the BOM or leave only whitespace in the first read.
      while (length < bytes.Length)
      {
        var read = stream.Read(bytes, length, bytes.Length - length);
        if (read == 0)
          break;
        length += read;
        if (DetectRoot(bytes.AsSpan(0, length)) != RootContainerKind.None)
          break;
      }
      return new StreamHead
      {
        RootKind = DetectRoot(bytes.AsSpan(0, length)),
        HeadBytes = origin >= 0 ? null : bytes.AsSpan(0, length).ToArray()
      };
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  internal static async ValueTask<StreamHead> PeekStreamHeadAsync(Stream stream, CancellationToken cancellationToken)
  {
    var origin = stream.CanSeek ? stream.Position : -1;
    var bytes = new byte[64];
    var length = 0;
    try
    {
      while (length < bytes.Length)
      {
        var read = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
          break;
        length += read;
        if (DetectRoot(bytes.AsSpan(0, length)) != RootContainerKind.None)
          break;
      }
      return new StreamHead
      {
        RootKind = DetectRoot(bytes.AsSpan(0, length)),
        HeadBytes = origin >= 0 ? null : bytes.AsSpan(0, length).ToArray()
      };
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  private static RootContainerKind DetectRoot(ReadOnlySpan<byte> bytes)
  {
    var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
    for (var i = start; i < bytes.Length; i++)
    {
      if (bytes[i] is 0x20 or 0x09 or 0x0A or 0x0D)
        continue;
      return bytes[i] switch
      {
        0x5B => RootContainerKind.Array,
        0x7B => RootContainerKind.Object,
        _ => RootContainerKind.None
      };
    }
    return RootContainerKind.None;
  }
}
