using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal static class JsonPathStreamingMatcher
{
  public static bool CanUseStreaming(Stream stream, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0 || !stream.CanSeek)
      return false;

    if (!TryPeekRootToken(stream, out var token) || token != '[')
      return false;

    var first = segments[0];
    return first.SegmentType switch
    {
      JsonPathSegmentType.Wildcard => true,
      JsonPathSegmentType.ArrayIndex => first.ArrayIndex >= 0,
      JsonPathSegmentType.ArrayRange => first.ArrayIndex >= 0 && (first.ArrayRangeEnd == int.MaxValue || first.ArrayRangeEnd >= 0),
      JsonPathSegmentType.ArrayUnion => first.ArrayUnionIndices is not null && first.ArrayUnionIndices.All(static i => i >= 0),
      JsonPathSegmentType.Filter => !string.IsNullOrWhiteSpace(first.FilterExpression),
      _ => false
    };
  }

  public static async Task<JsonNode?> ExtractFirstMatchAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var origin = stream.Position;
    try
    {
      var firstSegment = segments[0];
      var remainingSegments = segments.Skip(1).ToList();
      var index = 0;

      await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
      {
        if (!IsFirstSegmentMatch(firstSegment, index, item))
        {
          index++;
          continue;
        }

        var match = JsonPathExtractionCore.FindFirstMatch(item, remainingSegments);
        if (match is not null)
          return match;

        index++;
      }

      return null;
    }
    finally
    {
      stream.Position = origin;
    }
  }

  public static async IAsyncEnumerable<JsonNode?> ExtractAllMatchesAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var origin = stream.Position;
    try
    {
      var firstSegment = segments[0];
      var remainingSegments = segments.Skip(1).ToList();
      var index = 0;

      await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
      {
        if (IsFirstSegmentMatch(firstSegment, index, item))
        {
          foreach (var match in JsonPathExtractionCore.FindAllMatches(item, remainingSegments))
            yield return match;
        }

        index++;
      }
    }
    finally
    {
      stream.Position = origin;
    }
  }

  private static bool IsFirstSegmentMatch(JsonPathSegment first, int index, JsonNode? item)
  {
    return first.SegmentType switch
    {
      JsonPathSegmentType.Wildcard => true,
      JsonPathSegmentType.ArrayIndex => index == first.ArrayIndex,
      JsonPathSegmentType.ArrayRange => index >= first.ArrayIndex
        && (first.ArrayRangeEnd == int.MaxValue || index < first.ArrayRangeEnd),
      JsonPathSegmentType.ArrayUnion => first.ArrayUnionIndices is not null && Array.IndexOf(first.ArrayUnionIndices, index) >= 0,
      JsonPathSegmentType.Filter => JsonPathFilterEvaluator.Evaluate(item, first.FilterExpression!),
      _ => false
    };
  }

  private static bool TryPeekRootToken(Stream stream, out char token)
  {
    token = default;

    var origin = stream.Position;
    try
    {
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
      while (!reader.EndOfStream)
      {
        var ch = (char)reader.Read();
        if (char.IsWhiteSpace(ch))
          continue;

        token = ch;
        return true;
      }

      return false;
    }
    finally
    {
      stream.Position = origin;
    }
  }
}
