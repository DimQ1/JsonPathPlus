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
  private enum RootContainerKind
  {
    None,
    Array,
    Object
  }

  public static bool CanUseStreaming(Stream stream, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0 || !stream.CanSeek)
      return false;

    var rootKind = GetRootContainerKind(stream);
    if (rootKind == RootContainerKind.None)
      return false;

    var first = segments[0];
    return rootKind switch
    {
      RootContainerKind.Array => first.SegmentType switch
      {
        JsonPathSegmentType.Wildcard => true,
        JsonPathSegmentType.ArrayIndex => first.ArrayIndex >= 0,
        JsonPathSegmentType.ArrayRange => first.ArrayIndex >= 0 && (first.ArrayRangeEnd == int.MaxValue || first.ArrayRangeEnd >= 0),
        JsonPathSegmentType.ArrayUnion => first.ArrayUnionIndices is not null && first.ArrayUnionIndices.All(static i => i >= 0),
        JsonPathSegmentType.Filter => !string.IsNullOrWhiteSpace(first.FilterExpression),
        _ => false
      },
      RootContainerKind.Object => first.SegmentType switch
      {
        JsonPathSegmentType.Property => !string.IsNullOrWhiteSpace(first.PropertyName),
        JsonPathSegmentType.Wildcard => true,
        JsonPathSegmentType.PropertyUnion => first.PropertyUnionNames is { Length: > 0 },
        _ => false
      },
      _ => false
    };
  }

  public static async Task<JsonNode?> ExtractFirstMatchAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var origin = stream.Position;
    try
    {
      return GetRootContainerKind(stream) switch
      {
        RootContainerKind.Array => await ExtractFirstArrayRootMatchAsync(stream, segments),
        RootContainerKind.Object => await ExtractFirstObjectRootMatchAsync(stream, segments),
        _ => null
      };
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
      var rootKind = GetRootContainerKind(stream);
      if (rootKind == RootContainerKind.Array)
      {
        await foreach (var match in ExtractAllArrayRootMatchesAsync(stream, segments))
          yield return match;
      }
      else if (rootKind == RootContainerKind.Object)
      {
        await foreach (var match in ExtractAllObjectRootMatchesAsync(stream, segments))
          yield return match;
      }
    }
    finally
    {
      stream.Position = origin;
    }
  }

  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllMatchesWithPathsAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var origin = stream.Position;
    try
    {
      var rootKind = GetRootContainerKind(stream);
      if (rootKind == RootContainerKind.Array)
      {
        await foreach (var match in ExtractAllArrayRootMatchesWithPathsAsync(stream, segments))
          yield return match;
      }
      else if (rootKind == RootContainerKind.Object)
      {
        await foreach (var match in ExtractAllObjectRootMatchesWithPathsAsync(stream, segments))
          yield return match;
      }
    }
    finally
    {
      stream.Position = origin;
    }
  }

  private static async Task<JsonNode?> ExtractFirstArrayRootMatchAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (!IsArrayFirstSegmentMatch(firstSegment, index, item))
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

  private static async IAsyncEnumerable<JsonNode?> ExtractAllArrayRootMatchesAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (IsArrayFirstSegmentMatch(firstSegment, index, item))
      {
        foreach (var match in JsonPathExtractionCore.FindAllMatches(item, remainingSegments))
          yield return match;
      }

      index++;
    }
  }

  private static async IAsyncEnumerable<JsonPathMatch> ExtractAllArrayRootMatchesWithPathsAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (IsArrayFirstSegmentMatch(firstSegment, index, item))
      {
        var itemRootPath = $"$[{index}]";
        foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(item, remainingSegments, itemRootPath))
          yield return match;
      }

      index++;
    }
  }

  private static async Task<JsonNode?> ExtractFirstObjectRootMatchAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var bytes = await ReadToEndAsync(stream);
    return ExtractFirstObjectRootMatch(bytes, segments);
  }

  private static async IAsyncEnumerable<JsonNode?> ExtractAllObjectRootMatchesAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var bytes = await ReadToEndAsync(stream);
    foreach (var match in CollectAllObjectRootMatches(bytes, segments))
      yield return match;
  }

  private static async IAsyncEnumerable<JsonPathMatch> ExtractAllObjectRootMatchesWithPathsAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var bytes = await ReadToEndAsync(stream);
    foreach (var match in CollectAllObjectRootMatchesWithPaths(bytes, segments))
      yield return match;
  }

  private static JsonNode? ExtractFirstObjectRootMatch(byte[] bytes, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();

    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
      return null;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndObject)
        return null;

      if (reader.TokenType != JsonTokenType.PropertyName)
        continue;

      var propertyName = reader.GetString() ?? string.Empty;
      if (!reader.Read())
        return null;

      if (!IsObjectFirstSegmentMatch(firstSegment, propertyName))
      {
        reader.Skip();
        continue;
      }

      using var propertyDocument = JsonDocument.ParseValue(ref reader);
      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      var match = JsonPathExtractionCore.FindFirstMatch(propertyNode, remainingSegments);
      if (match is not null)
        return match;
    }

    return null;
  }

  private static List<JsonNode?> CollectAllObjectRootMatches(byte[] bytes, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();
    var results = new List<JsonNode?>();

    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
      return results;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndObject)
        return results;

      if (reader.TokenType != JsonTokenType.PropertyName)
        continue;

      var propertyName = reader.GetString() ?? string.Empty;
      if (!reader.Read())
        return results;

      if (!IsObjectFirstSegmentMatch(firstSegment, propertyName))
      {
        reader.Skip();
        continue;
      }

      using var propertyDocument = JsonDocument.ParseValue(ref reader);
      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      foreach (var match in JsonPathExtractionCore.FindAllMatches(propertyNode, remainingSegments))
        results.Add(match);
    }

    return results;
  }

  private static List<JsonPathMatch> CollectAllObjectRootMatchesWithPaths(byte[] bytes, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    var remainingSegments = segments.Skip(1).ToList();
    var results = new List<JsonPathMatch>();

    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
      return results;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndObject)
        return results;

      if (reader.TokenType != JsonTokenType.PropertyName)
        continue;

      var propertyName = reader.GetString() ?? string.Empty;
      if (!reader.Read())
        return results;

      if (!IsObjectFirstSegmentMatch(firstSegment, propertyName))
      {
        reader.Skip();
        continue;
      }

      using var propertyDocument = JsonDocument.ParseValue(ref reader);
      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      var propertyPath = AppendPropertyPath("$", propertyName);
      foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(propertyNode, remainingSegments, propertyPath))
        results.Add(match);
    }

    return results;
  }

  private static bool IsArrayFirstSegmentMatch(JsonPathSegment first, int index, JsonNode? item)
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

  private static bool IsObjectFirstSegmentMatch(JsonPathSegment first, string propertyName)
  {
    return first.SegmentType switch
    {
      JsonPathSegmentType.Property => string.Equals(first.PropertyName, propertyName, StringComparison.Ordinal),
      JsonPathSegmentType.Wildcard => true,
      JsonPathSegmentType.PropertyUnion => first.PropertyUnionNames is not null
        && Array.IndexOf(first.PropertyUnionNames, propertyName) >= 0,
      _ => false
    };
  }

  private static async Task<byte[]> ReadToEndAsync(Stream stream)
  {
    using var copy = new MemoryStream();
    await stream.CopyToAsync(copy);
    return copy.ToArray();
  }

  private static RootContainerKind GetRootContainerKind(Stream stream)
  {
    if (!TryPeekRootToken(stream, out var token))
      return RootContainerKind.None;

    return token switch
    {
      '[' => RootContainerKind.Array,
      '{' => RootContainerKind.Object,
      _ => RootContainerKind.None
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

  private static string AppendPropertyPath(string parentPath, string propertyName)
  {
    if (IsSimpleIdentifier(propertyName))
      return $"{parentPath}.{propertyName}";

    var escapedProperty = propertyName
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("'", "\\'", StringComparison.Ordinal);
    return $"{parentPath}['{escapedProperty}']";
  }

  private static bool IsSimpleIdentifier(string propertyName)
  {
    if (string.IsNullOrEmpty(propertyName))
      return false;

    if (!(char.IsLetter(propertyName[0]) || propertyName[0] == '_'))
      return false;

    for (var i = 1; i < propertyName.Length; i++)
    {
      var ch = propertyName[i];
      if (!(char.IsLetterOrDigit(ch) || ch == '_'))
        return false;
    }

    return true;
  }
}
