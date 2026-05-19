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

  private readonly record struct ElementPathMatch(JsonElement Element, string Path);

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

    if (CanUseElementArrayStreaming(firstSegment, remainingSegments))
      return await ExtractFirstArrayRootElementMatchAsync(stream, firstSegment, remainingSegments);

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

    if (CanUseElementArrayStreaming(firstSegment, remainingSegments))
    {
      await foreach (var match in ExtractAllArrayRootElementMatchesAsync(stream, firstSegment, remainingSegments))
        yield return match;

      yield break;
    }

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

    if (CanUseElementArrayStreaming(firstSegment, remainingSegments))
    {
      await foreach (var match in ExtractAllArrayRootElementMatchesWithPathsAsync(stream, firstSegment, remainingSegments))
        yield return match;

      yield break;
    }

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

  private static async Task<JsonNode?> ExtractFirstArrayRootElementMatchAsync(
    Stream stream,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> remainingSegments)
  {
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream))
    {
      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        index++;
        continue;
      }

      if (remainingSegments.Count == 0)
        return MaterializeElement(item);

      TryFindElementMatches(item, remainingSegments, out var elementMatches);
      if (elementMatches.Count > 0)
        return MaterializeElement(elementMatches[0]);

      index++;
    }

    return null;
  }

  private static async IAsyncEnumerable<JsonNode?> ExtractAllArrayRootElementMatchesAsync(
    Stream stream,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> remainingSegments)
  {
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream))
    {
      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        index++;
        continue;
      }

      if (remainingSegments.Count == 0)
      {
        yield return MaterializeElement(item);
        index++;
        continue;
      }

      TryFindElementMatches(item, remainingSegments, out var elementMatches);
      foreach (var elementMatch in elementMatches)
        yield return MaterializeElement(elementMatch);

      index++;
    }
  }

  private static async IAsyncEnumerable<JsonPathMatch> ExtractAllArrayRootElementMatchesWithPathsAsync(
    Stream stream,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> remainingSegments)
  {
    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream))
    {
      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        index++;
        continue;
      }

      var itemRootPath = $"$[{index}]";
      if (remainingSegments.Count == 0)
      {
        yield return new JsonPathMatch(itemRootPath, MaterializeElement(item));
        index++;
        continue;
      }

      TryFindElementMatchesWithPaths(item, remainingSegments, itemRootPath, out var elementMatches);
      foreach (var elementMatch in elementMatches)
        yield return new JsonPathMatch(elementMatch.Path, MaterializeElement(elementMatch.Element));

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
      if (TryFindElementMatches(propertyDocument.RootElement, remainingSegments, out var elementMatches))
      {
        if (elementMatches.Count > 0)
          return MaterializeElement(elementMatches[0]);

        continue;
      }

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
      if (TryFindElementMatches(propertyDocument.RootElement, remainingSegments, out var elementMatches))
      {
        foreach (var elementMatch in elementMatches)
          results.Add(MaterializeElement(elementMatch));

        continue;
      }

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

      var propertyPath = AppendPropertyPath("$", propertyName);
      using var propertyDocument = JsonDocument.ParseValue(ref reader);
      if (TryFindElementMatchesWithPaths(propertyDocument.RootElement, remainingSegments, propertyPath, out var elementMatches))
      {
        foreach (var elementMatch in elementMatches)
          results.Add(new JsonPathMatch(elementMatch.Path, MaterializeElement(elementMatch.Element)));

        continue;
      }

      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
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

  private static bool IsArrayFirstSegmentElementMatch(JsonPathSegment first, int index)
  {
    return first.SegmentType switch
    {
      JsonPathSegmentType.Wildcard => true,
      JsonPathSegmentType.ArrayIndex => index == first.ArrayIndex,
      JsonPathSegmentType.ArrayRange => index >= first.ArrayIndex
        && (first.ArrayRangeEnd == int.MaxValue || index < first.ArrayRangeEnd),
      JsonPathSegmentType.ArrayUnion => first.ArrayUnionIndices is not null && Array.IndexOf(first.ArrayUnionIndices, index) >= 0,
      _ => false
    };
  }

  private static bool CanUseElementArrayStreaming(
    JsonPathSegment firstSegment,
    List<JsonPathSegment> remainingSegments)
  {
    if (firstSegment.SegmentType == JsonPathSegmentType.Filter)
      return false;

    foreach (var segment in remainingSegments)
    {
      switch (segment.SegmentType)
      {
        case JsonPathSegmentType.Property:
        case JsonPathSegmentType.ArrayIndex:
        case JsonPathSegmentType.ArrayRange:
        case JsonPathSegmentType.Wildcard:
          continue;

        default:
          return false;
      }
    }

    return true;
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

  private static bool TryFindElementMatches(
    JsonElement root,
    List<JsonPathSegment> segments,
    out List<JsonElement> matches)
  {
    var current = new List<JsonElement> { root };

    foreach (var segment in segments)
    {
      var next = new List<JsonElement>();

      foreach (var element in current)
      {
        if (!TryCollectElementMatches(element, segment, next))
        {
          matches = new List<JsonElement>();
          return false;
        }
      }

      current = next;
      if (current.Count == 0)
        break;
    }

    matches = current;
    return true;
  }

  private static bool TryFindElementMatchesWithPaths(
    JsonElement root,
    List<JsonPathSegment> segments,
    string rootPath,
    out List<ElementPathMatch> matches)
  {
    var current = new List<ElementPathMatch> { new(root, rootPath) };

    foreach (var segment in segments)
    {
      var next = new List<ElementPathMatch>();

      foreach (var currentMatch in current)
      {
        if (!TryCollectElementMatchesWithPaths(currentMatch, segment, next))
        {
          matches = new List<ElementPathMatch>();
          return false;
        }
      }

      current = next;
      if (current.Count == 0)
        break;
    }

    matches = current;
    return true;
  }

  private static bool TryCollectElementMatches(
    JsonElement element,
    JsonPathSegment segment,
    List<JsonElement> results)
  {
    switch (segment.SegmentType)
    {
      case JsonPathSegmentType.Property:
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(segment.PropertyName!, out var propertyValue))
        {
          results.Add(propertyValue);
        }

        return true;

      case JsonPathSegmentType.ArrayIndex:
        if (TryGetArrayElement(element, segment.ArrayIndex, out var arrayElement))
          results.Add(arrayElement);

        return true;

      case JsonPathSegmentType.ArrayRange:
        CollectArrayRangeElements(element, segment.ArrayIndex, segment.ArrayRangeEnd, results);
        return true;

      case JsonPathSegmentType.Wildcard:
        CollectWildcardElements(element, results);
        return true;

      default:
        return false;
    }
  }

  private static bool TryCollectElementMatchesWithPaths(
    ElementPathMatch currentMatch,
    JsonPathSegment segment,
    List<ElementPathMatch> results)
  {
    switch (segment.SegmentType)
    {
      case JsonPathSegmentType.Property:
        if (currentMatch.Element.ValueKind == JsonValueKind.Object &&
            currentMatch.Element.TryGetProperty(segment.PropertyName!, out var propertyValue))
        {
          results.Add(new ElementPathMatch(
            propertyValue,
            AppendPropertyPath(currentMatch.Path, segment.PropertyName!)));
        }

        return true;

      case JsonPathSegmentType.ArrayIndex:
        if (TryGetArrayElement(currentMatch.Element, segment.ArrayIndex, out var arrayElement, out var effectiveIndex))
        {
          results.Add(new ElementPathMatch(
            arrayElement,
            AppendArrayIndexPath(currentMatch.Path, effectiveIndex)));
        }

        return true;

      case JsonPathSegmentType.ArrayRange:
        CollectArrayRangeElementsWithPaths(currentMatch, segment.ArrayIndex, segment.ArrayRangeEnd, results);
        return true;

      case JsonPathSegmentType.Wildcard:
        CollectWildcardElementsWithPaths(currentMatch, results);
        return true;

      default:
        return false;
    }
  }

  private static bool TryGetArrayElement(JsonElement element, int index, out JsonElement match)
  {
    return TryGetArrayElement(element, index, out match, out _);
  }

  private static bool TryGetArrayElement(JsonElement element, int index, out JsonElement match, out int effectiveIndex)
  {
    match = default;
    effectiveIndex = -1;

    if (element.ValueKind != JsonValueKind.Array)
      return false;

    var length = element.GetArrayLength();
    effectiveIndex = index < 0 ? length + index : index;
    if (effectiveIndex < 0 || effectiveIndex >= length)
      return false;

    var currentIndex = 0;
    foreach (var item in element.EnumerateArray())
    {
      if (currentIndex == effectiveIndex)
      {
        match = item;
        return true;
      }

      currentIndex++;
    }

    return false;
  }

  private static void CollectArrayRangeElements(
    JsonElement element,
    int start,
    int endExclusive,
    List<JsonElement> results)
  {
    if (element.ValueKind != JsonValueKind.Array)
      return;

    var length = element.GetArrayLength();
    var effectiveStart = start < 0 ? length + start : start;
    var effectiveEnd = endExclusive == int.MaxValue
      ? length
      : (endExclusive < 0 ? length + endExclusive : endExclusive);

    effectiveStart = Math.Clamp(effectiveStart, 0, length);
    effectiveEnd = Math.Clamp(effectiveEnd, 0, length);
    if (effectiveEnd < effectiveStart)
      return;

    var currentIndex = 0;
    foreach (var item in element.EnumerateArray())
    {
      if (currentIndex >= effectiveEnd)
        break;

      if (currentIndex >= effectiveStart)
        results.Add(item);

      currentIndex++;
    }
  }

  private static void CollectArrayRangeElementsWithPaths(
    ElementPathMatch currentMatch,
    int start,
    int endExclusive,
    List<ElementPathMatch> results)
  {
    var element = currentMatch.Element;
    if (element.ValueKind != JsonValueKind.Array)
      return;

    var length = element.GetArrayLength();
    var effectiveStart = start < 0 ? length + start : start;
    var effectiveEnd = endExclusive == int.MaxValue
      ? length
      : (endExclusive < 0 ? length + endExclusive : endExclusive);

    effectiveStart = Math.Clamp(effectiveStart, 0, length);
    effectiveEnd = Math.Clamp(effectiveEnd, 0, length);
    if (effectiveEnd < effectiveStart)
      return;

    var currentIndex = 0;
    foreach (var item in element.EnumerateArray())
    {
      if (currentIndex >= effectiveEnd)
        break;

      if (currentIndex >= effectiveStart)
      {
        results.Add(new ElementPathMatch(
          item,
          AppendArrayIndexPath(currentMatch.Path, currentIndex)));
      }

      currentIndex++;
    }
  }

  private static void CollectWildcardElements(JsonElement element, List<JsonElement> results)
  {
    if (element.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in element.EnumerateArray())
        results.Add(item);

      return;
    }

    if (element.ValueKind != JsonValueKind.Object)
      return;

    foreach (var property in element.EnumerateObject())
      results.Add(property.Value);
  }

  private static void CollectWildcardElementsWithPaths(
    ElementPathMatch currentMatch,
    List<ElementPathMatch> results)
  {
    if (currentMatch.Element.ValueKind == JsonValueKind.Array)
    {
      var index = 0;
      foreach (var item in currentMatch.Element.EnumerateArray())
      {
        results.Add(new ElementPathMatch(item, AppendArrayIndexPath(currentMatch.Path, index)));
        index++;
      }

      return;
    }

    if (currentMatch.Element.ValueKind != JsonValueKind.Object)
      return;

    foreach (var property in currentMatch.Element.EnumerateObject())
    {
      results.Add(new ElementPathMatch(
        property.Value,
        AppendPropertyPath(currentMatch.Path, property.Name)));
    }
  }

  private static JsonNode? MaterializeElement(JsonElement element)
  {
    return element.ValueKind switch
    {
      JsonValueKind.Null => null,
      JsonValueKind.Undefined => null,
      JsonValueKind.String => JsonValue.Create(element.GetString()),
      JsonValueKind.True => JsonValue.Create(true),
      JsonValueKind.False => JsonValue.Create(false),
      _ => JsonNode.Parse(element.GetRawText())
    };
  }

  private static async Task<byte[]> ReadToEndAsync(Stream stream)
  {
    if (stream.CanSeek)
    {
      var remainingLength = stream.Length - stream.Position;
      if (remainingLength <= 0)
        return Array.Empty<byte>();

      if (remainingLength <= int.MaxValue)
      {
        var buffer = new byte[(int)remainingLength];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
          var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead));
          if (bytesRead == 0)
            break;

          totalRead += bytesRead;
        }

        if (totalRead == buffer.Length)
          return buffer;

        Array.Resize(ref buffer, totalRead);
        return buffer;
      }
    }

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

  private static string AppendArrayIndexPath(string parentPath, int index)
    => $"{parentPath}[{index}]";

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
