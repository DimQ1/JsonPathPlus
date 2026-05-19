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
  internal enum RootContainerKind
  {
    None,
    Array,
    Object
  }

  private readonly record struct ElementPathMatch(JsonElement Element, string Path);

  /// <summary>
  /// Captures the root container kind and any bytes consumed during the initial peek,
  /// so downstream extraction can continue from the correct position without re-seeking.
  /// </summary>
  public readonly record struct StreamHead
  {
    public RootContainerKind RootKind { get; init; }
    public byte[]? HeadBytes { get; init; }
    public bool IsEmpty => RootKind == RootContainerKind.None;
  }

  /// <summary>
  /// Determines whether the stream can use the optimized streaming path.
  /// Returns a <see cref="StreamHead"/> if streaming is possible, or <c>null</c> otherwise.
  /// The caller must pass the returned <see cref="StreamHead"/> to the corresponding extract method.
  /// Does not require the stream to be seekable.
  /// </summary>
  public static StreamHead? CanUseStreaming(Stream stream, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0)
      return null;

    var head = PeekStreamHead(stream);
    if (head.IsEmpty)
      return null;

    var first = segments[0];
    var canStream = head.RootKind switch
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

    return canStream ? head : null;
  }

  /// <summary>
  /// Extracts the first match using the pre-peeked stream head.
  /// The stream position must be immediately after the bytes captured in <paramref name="head"/>.
  /// For seekable streams, the position is restored after extraction.
  /// </summary>
  public static async Task<JsonNode?> ExtractFirstMatchAsync(Stream stream, List<JsonPathSegment> segments, StreamHead head)
  {
    var origin = stream.CanSeek ? stream.Position : -1;
    try
    {
      using var effectiveStream = head.HeadBytes is { Length: > 0 }
        ? CreateHeadPrependedStream(head.HeadBytes, stream)
        : null;

      var targetStream = effectiveStream ?? stream;

      return head.RootKind switch
      {
        RootContainerKind.Array => await ExtractFirstArrayRootMatchAsync(targetStream, segments),
        RootContainerKind.Object => await ExtractFirstObjectRootMatchAsync(targetStream, segments),
        _ => null
      };
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  /// <summary>
  /// Extracts all matches using the pre-peeked stream head.
  /// The stream position must be immediately after the bytes captured in <paramref name="head"/>.
  /// For seekable streams, the position is restored after extraction.
  /// </summary>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllMatchesAsync(Stream stream, List<JsonPathSegment> segments, StreamHead head)
  {
    var origin = stream.CanSeek ? stream.Position : -1;
    try
    {
      using var effectiveStream = head.HeadBytes is { Length: > 0 }
        ? CreateHeadPrependedStream(head.HeadBytes, stream)
        : null;

      var targetStream = effectiveStream ?? stream;

      if (head.RootKind == RootContainerKind.Array)
      {
        await foreach (var match in ExtractAllArrayRootMatchesAsync(targetStream, segments))
          yield return match;
      }
      else if (head.RootKind == RootContainerKind.Object)
      {
        await foreach (var match in ExtractAllObjectRootMatchesAsync(targetStream, segments))
          yield return match;
      }
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  /// <summary>
  /// Extracts all matches with paths using the pre-peeked stream head.
  /// The stream position must be immediately after the bytes captured in <paramref name="head"/>.
  /// For seekable streams, the position is restored after extraction.
  /// </summary>
  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllMatchesWithPathsAsync(Stream stream, List<JsonPathSegment> segments, StreamHead head)
  {
    var origin = stream.CanSeek ? stream.Position : -1;
    try
    {
      using var effectiveStream = head.HeadBytes is { Length: > 0 }
        ? CreateHeadPrependedStream(head.HeadBytes, stream)
        : null;

      var targetStream = effectiveStream ?? stream;

      if (head.RootKind == RootContainerKind.Array)
      {
        await foreach (var match in ExtractAllArrayRootMatchesWithPathsAsync(targetStream, segments))
          yield return match;
      }
      else if (head.RootKind == RootContainerKind.Object)
      {
        await foreach (var match in ExtractAllObjectRootMatchesWithPathsAsync(targetStream, segments))
          yield return match;
      }
    }
    finally
    {
      if (origin >= 0)
        stream.Position = origin;
    }
  }

  /// <summary>
  /// Creates a stream that yields <paramref name="headBytes"/> first, then the remainder of <paramref name="innerStream"/>.
  /// </summary>
  private static Stream CreateHeadPrependedStream(byte[] headBytes, Stream innerStream)
  {
    var combined = new MemoryStream(headBytes.Length + (innerStream.CanSeek ? (int)(innerStream.Length - innerStream.Position) : 0));
    combined.Write(headBytes, 0, headBytes.Length);
    innerStream.CopyTo(combined);
    combined.Position = 0;
    return combined;
  }

  private static async Task<JsonNode?> ExtractFirstArrayRootMatchAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    const int remainingSegmentStartIndex = 1;

    if (CanUseElementArrayStreaming(segments, remainingSegmentStartIndex))
    {
      var bytes = await ReadToEndAsync(stream);
      return ExtractFirstArrayRootElementMatch(bytes, firstSegment, segments, remainingSegmentStartIndex);
    }

    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (!IsArrayFirstSegmentMatch(firstSegment, index, item))
      {
        index++;
        continue;
      }

      var match = JsonPathExtractionCore.FindFirstMatch(item, segments, remainingSegmentStartIndex);
      if (match is not null)
        return match;

      index++;
    }

    return null;
  }

  private static async IAsyncEnumerable<JsonNode?> ExtractAllArrayRootMatchesAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    const int remainingSegmentStartIndex = 1;

    if (CanUseElementArrayStreaming(segments, remainingSegmentStartIndex))
    {
      var bytes = await ReadToEndAsync(stream);
      foreach (var match in CollectAllArrayRootElementMatches(bytes, firstSegment, segments, remainingSegmentStartIndex))
        yield return match;

      yield break;
    }

    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (IsArrayFirstSegmentMatch(firstSegment, index, item))
      {
        foreach (var match in JsonPathExtractionCore.FindAllMatches(item, segments, remainingSegmentStartIndex))
          yield return match;
      }

      index++;
    }
  }

  private static async IAsyncEnumerable<JsonPathMatch> ExtractAllArrayRootMatchesWithPathsAsync(Stream stream, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    const int remainingSegmentStartIndex = 1;

    if (CanUseElementArrayStreaming(segments, remainingSegmentStartIndex))
    {
      var bytes = await ReadToEndAsync(stream);
      foreach (var match in CollectAllArrayRootElementMatchesWithPaths(bytes, firstSegment, segments, remainingSegmentStartIndex))
        yield return match;

      yield break;
    }

    var index = 0;

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream))
    {
      if (IsArrayFirstSegmentMatch(firstSegment, index, item))
      {
        var itemRootPath = $"$[{index}]";
        foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(item, segments, remainingSegmentStartIndex, itemRootPath))
          yield return match;
      }

      index++;
    }
  }

  private static JsonNode? ExtractFirstArrayRootElementMatch(
    byte[] bytes,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> segments,
    int segmentStartIndex)
  {
    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
      return null;

    var index = 0;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndArray)
        return null;

      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        SkipCurrentValue(ref reader);
        index++;
        continue;
      }

      using var itemDocument = JsonDocument.ParseValue(ref reader);
      var item = itemDocument.RootElement;

      if (segmentStartIndex >= segments.Count)
        return MaterializeElement(item);

      TryFindElementMatches(item, segments, segmentStartIndex, out var elementMatches);
      if (elementMatches.Count > 0)
        return MaterializeElement(elementMatches[0]);

      index++;
    }

    return null;
  }

  private static List<JsonNode?> CollectAllArrayRootElementMatches(
    byte[] bytes,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> segments,
    int segmentStartIndex)
  {
    var results = new List<JsonNode?>();
    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
      return results;

    var index = 0;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndArray)
        return results;

      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        SkipCurrentValue(ref reader);
        index++;
        continue;
      }

      using var itemDocument = JsonDocument.ParseValue(ref reader);
      var item = itemDocument.RootElement;

      if (segmentStartIndex >= segments.Count)
      {
        results.Add(MaterializeElement(item));
        index++;
        continue;
      }

      TryFindElementMatches(item, segments, segmentStartIndex, out var elementMatches);
      foreach (var elementMatch in elementMatches)
        results.Add(MaterializeElement(elementMatch));

      index++;
    }

    return results;
  }

  private static List<JsonPathMatch> CollectAllArrayRootElementMatchesWithPaths(
    byte[] bytes,
    JsonPathSegment firstSegment,
    List<JsonPathSegment> segments,
    int segmentStartIndex)
  {
    var results = new List<JsonPathMatch>();
    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
      return results;

    var index = 0;

    while (reader.Read())
    {
      if (reader.TokenType == JsonTokenType.EndArray)
        return results;

      if (!IsArrayFirstSegmentElementMatch(firstSegment, index))
      {
        SkipCurrentValue(ref reader);
        index++;
        continue;
      }

      var itemRootPath = $"$[{index}]";
      using var itemDocument = JsonDocument.ParseValue(ref reader);
      var item = itemDocument.RootElement;

      if (segmentStartIndex >= segments.Count)
      {
        results.Add(new JsonPathMatch(itemRootPath, MaterializeElement(item)));
        index++;
        continue;
      }

      TryFindElementMatchesWithPaths(item, segments, segmentStartIndex, itemRootPath, out var elementMatches);
      foreach (var elementMatch in elementMatches)
        results.Add(new JsonPathMatch(elementMatch.Path, MaterializeElement(elementMatch.Element)));

      index++;
    }

    return results;
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
    const int remainingSegmentStartIndex = 1;

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
      if (TryFindElementMatches(propertyDocument.RootElement, segments, remainingSegmentStartIndex, out var elementMatches))
      {
        if (elementMatches.Count > 0)
          return MaterializeElement(elementMatches[0]);

        continue;
      }

      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      var match = JsonPathExtractionCore.FindFirstMatch(propertyNode, segments, remainingSegmentStartIndex);
      if (match is not null)
        return match;
    }

    return null;
  }

  private static List<JsonNode?> CollectAllObjectRootMatches(byte[] bytes, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    const int remainingSegmentStartIndex = 1;
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
      if (TryFindElementMatches(propertyDocument.RootElement, segments, remainingSegmentStartIndex, out var elementMatches))
      {
        foreach (var elementMatch in elementMatches)
          results.Add(MaterializeElement(elementMatch));

        continue;
      }

      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      foreach (var match in JsonPathExtractionCore.FindAllMatches(propertyNode, segments, remainingSegmentStartIndex))
        results.Add(match);
    }

    return results;
  }

  private static List<JsonPathMatch> CollectAllObjectRootMatchesWithPaths(byte[] bytes, List<JsonPathSegment> segments)
  {
    var firstSegment = segments[0];
    const int remainingSegmentStartIndex = 1;
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
      if (TryFindElementMatchesWithPaths(propertyDocument.RootElement, segments, remainingSegmentStartIndex, propertyPath, out var elementMatches))
      {
        foreach (var elementMatch in elementMatches)
          results.Add(new JsonPathMatch(elementMatch.Path, MaterializeElement(elementMatch.Element)));

        continue;
      }

      var propertyNode = JsonNode.Parse(propertyDocument.RootElement.GetRawText());
      foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(propertyNode, segments, remainingSegmentStartIndex, propertyPath))
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
    List<JsonPathSegment> segments,
    int segmentStartIndex)
  {
    var firstSegment = segments[0];
    if (firstSegment.SegmentType == JsonPathSegmentType.Filter)
      return false;

    for (var segmentIndex = segmentStartIndex; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
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
    int startIndex,
    out List<JsonElement> matches)
  {
    var current = new List<JsonElement> { root };

    for (var segmentIndex = startIndex; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
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
    int startIndex,
    string rootPath,
    out List<ElementPathMatch> matches)
  {
    var current = new List<ElementPathMatch> { new(root, rootPath) };

    for (var segmentIndex = startIndex; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
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

  private static void SkipCurrentValue(ref Utf8JsonReader reader)
  {
    if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
      reader.Skip();
  }

  private static async Task<byte[]> ReadToEndAsync(Stream stream)
  {
    return await ReadToEndAsync(stream, head: null);
  }

  private static async Task<byte[]> ReadToEndAsync(Stream stream, byte[]? head)
  {
    byte[]? remaining = null;

    try
    {
      if (stream.CanSeek)
      {
        var remainingLength = stream.Length - stream.Position;
        if (remainingLength <= 0)
          return head ?? Array.Empty<byte>();

        if (remainingLength <= int.MaxValue)
        {
          remaining = new byte[(int)remainingLength];
          var totalRead = 0;

          while (totalRead < remaining.Length)
          {
            var bytesRead = await stream.ReadAsync(remaining.AsMemory(totalRead));
            if (bytesRead == 0)
              break;

            totalRead += bytesRead;
          }

          if (totalRead != remaining.Length)
            Array.Resize(ref remaining, totalRead);
        }
      }

      if (remaining is null)
      {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        remaining = copy.ToArray();
      }
    }
    catch
    {
      // If we allocated 'remaining' and concatenation fails, let the caller handle it
      throw;
    }

    if (head is null || head.Length == 0)
      return remaining;

    var result = new byte[head.Length + remaining.Length];
    Array.Copy(head, 0, result, 0, head.Length);
    Array.Copy(remaining, 0, result, head.Length, remaining.Length);
    return result;
  }

  /// <summary>
  /// Peeks the stream to determine the root container kind and captures any bytes consumed.
  /// For seekable streams, restores position and returns an empty head-bytes array.
  /// For non-seekable streams, returns the consumed bytes so the caller can prepend them.
  /// </summary>
  private static StreamHead PeekStreamHead(Stream stream)
  {
    const int maxPeekBytes = 64;

    if (stream.CanSeek)
    {
      var origin = stream.Position;
      try
      {
        var kind = PeekRootKindFromStream(stream, maxPeekBytes);
        return new StreamHead { RootKind = kind, HeadBytes = null };
      }
      finally
      {
        stream.Position = origin;
      }
    }

    // Non-seekable: consume bytes and return them as head
    var headBuffer = new byte[maxPeekBytes];
    var totalRead = 0;
    var bytesRead = stream.Read(headBuffer, 0, maxPeekBytes);
    if (bytesRead == 0)
      return new StreamHead { RootKind = RootContainerKind.None, HeadBytes = Array.Empty<byte>() };

    totalRead = bytesRead;

    var detectedKind = PeekRootKindFromBytes(headBuffer.AsSpan(0, totalRead));
    if (totalRead == headBuffer.Length)
      return new StreamHead { RootKind = detectedKind, HeadBytes = headBuffer };

    // Trim to actual read size
    var trimmed = new byte[totalRead];
    Array.Copy(headBuffer, trimmed, totalRead);
    return new StreamHead { RootKind = detectedKind, HeadBytes = trimmed };
  }

  private static RootContainerKind PeekRootKindFromStream(Stream stream, int maxBytes)
  {
    Span<byte> buffer = stackalloc byte[maxBytes];
    var bytesRead = stream.Read(buffer);
    if (bytesRead == 0)
      return RootContainerKind.None;

    return PeekRootKindFromBytes(buffer[..bytesRead]);
  }

  private static RootContainerKind PeekRootKindFromBytes(ReadOnlySpan<byte> bytes)
  {
    for (var i = 0; i < bytes.Length; i++)
    {
      var b = bytes[i];

      // Skip UTF-8 BOM
      if (i == 0 && b == 0xEF && bytes.Length > 2 && bytes[1] == 0xBB && bytes[2] == 0xBF)
      {
        i += 2;
        continue;
      }

      // Skip whitespace (space, tab, newline, carriage return)
      if (b is 0x20 or 0x09 or 0x0A or 0x0D)
        continue;

      return b switch
      {
        0x5B => RootContainerKind.Array,  // '['
        0x7B => RootContainerKind.Object, // '{'
        _ => RootContainerKind.None
      };
    }

    return RootContainerKind.None;
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
