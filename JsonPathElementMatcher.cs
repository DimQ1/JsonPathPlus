using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal readonly record struct JsonPathValueMatch(string Path, JsonNode? Value);

internal static class JsonPathElementMatcher
{
  private readonly record struct ElementMatch(JsonElement Element, JsonPathLocation? Path);

  public static JsonNode? Materialize(JsonElement element)
    => element.ValueKind switch
    {
      JsonValueKind.Null => null,
      JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<JsonNode>(element),
      _ => JsonValue.Create(element.Clone())
    };

  public static IEnumerable<JsonPathValueMatch> FindMatches(
    JsonElement element, List<JsonPathSegment> segments, int startIndex, JsonPathLocation? path)
  {
    while (startIndex < segments.Count && segments[startIndex].SegmentType == JsonPathSegmentType.Property)
    {
      var name = segments[startIndex].PropertyName!;
      if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
        yield break;
      path = path is null ? null : PropertyPath(path, name);
      startIndex++;
    }

    if (startIndex >= segments.Count)
    {
      yield return new JsonPathValueMatch(path?.ToString() ?? "$", Materialize(element));
      yield break;
    }

    var segment = segments[startIndex];
    if (!IsSupported(segment.SegmentType))
    {
      var node = Materialize(element);
      if (path is null)
      {
        foreach (var match in JsonPathExtractionCore.FindAllMatches(node, segments, startIndex))
          yield return new JsonPathValueMatch("$", match);
      }
      else
      {
        foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(node, segments, startIndex, path.ToString()))
          yield return new JsonPathValueMatch(match.Path, match.Value);
      }
      yield break;
    }

    foreach (var child in Select(element, segment, path))
    {
      foreach (var match in FindMatches(child.Element, segments, startIndex + 1, path is null ? null : child.Path))
        yield return match;
    }
  }

  private static bool IsSupported(JsonPathSegmentType type)
    => type is JsonPathSegmentType.Property or JsonPathSegmentType.ArrayIndex
      or JsonPathSegmentType.ArrayRange or JsonPathSegmentType.ArrayUnion
      or JsonPathSegmentType.PropertyUnion or JsonPathSegmentType.ComputedIndex
      or JsonPathSegmentType.Wildcard or JsonPathSegmentType.Filter;

  private static IEnumerable<ElementMatch> Select(JsonElement element, JsonPathSegment segment, JsonPathLocation? path)
  {
    if (segment.SegmentType == JsonPathSegmentType.Property)
    {
      if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment.PropertyName!, out var value))
        yield return new ElementMatch(value, PropertyPath(path, segment.PropertyName!));
      yield break;
    }

    if (segment.SegmentType == JsonPathSegmentType.PropertyUnion)
    {
      if (element.ValueKind == JsonValueKind.Object && segment.PropertyUnionNames is not null)
      {
        foreach (var name in segment.PropertyUnionNames)
        {
          if (element.TryGetProperty(name, out var value))
            yield return new ElementMatch(value, PropertyPath(path, name));
        }
      }
      yield break;
    }

    var filter = segment.SegmentType == JsonPathSegmentType.Filter
      ? segment.FilterNode ?? JsonPathFilterNode.Compile(segment.FilterExpression ?? string.Empty)
      : null;

    if (element.ValueKind == JsonValueKind.Object)
    {
      if (segment.SegmentType is JsonPathSegmentType.Wildcard or JsonPathSegmentType.Filter)
      {
        foreach (var property in element.EnumerateObject())
        {
          if (filter is null || filter.Evaluate(property.Value))
            yield return new ElementMatch(property.Value, PropertyPath(path, property.Name));
        }
      }
      yield break;
    }

    if (element.ValueKind != JsonValueKind.Array)
      yield break;

    var length = element.GetArrayLength();
    if (segment.SegmentType == JsonPathSegmentType.ArrayUnion)
    {
      if (segment.ArrayUnionIndices is not null)
      {
        foreach (var index in segment.ArrayUnionIndices)
        {
          var effective = index < 0 ? length + index : index;
          if (effective >= 0 && effective < length)
            yield return new ElementMatch(element[effective], ArrayPath(path, effective));
        }
      }
      yield break;
    }

    if (segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ComputedIndex)
    {
      var index = segment.ArrayIndex;
      if (segment.SegmentType == JsonPathSegmentType.ComputedIndex
          && !JsonPathComputedExpressionEvaluator.TryEvaluateIndex(length, segment.ComputedIndexExpression ?? string.Empty, out index))
        yield break;

      var effective = index < 0 ? length + index : index;
      if (effective >= 0 && effective < length)
        yield return new ElementMatch(element[effective], ArrayPath(path, effective));
      yield break;
    }

    var start = 0;
    var end = length;
    if (segment.SegmentType == JsonPathSegmentType.ArrayRange)
    {
      start = Math.Clamp(segment.ArrayIndex < 0 ? length + segment.ArrayIndex : segment.ArrayIndex, 0, length);
      end = segment.ArrayRangeEnd == int.MaxValue ? length
        : Math.Clamp(segment.ArrayRangeEnd < 0 ? length + segment.ArrayRangeEnd : segment.ArrayRangeEnd, 0, length);
    }

    var currentIndex = 0;
    foreach (var item in element.EnumerateArray())
    {
      if (currentIndex >= end)
        yield break;
      if (currentIndex >= start && (filter is null || filter.Evaluate(item)))
        yield return new ElementMatch(item, ArrayPath(path, currentIndex));
      currentIndex++;
    }
  }

  internal static JsonPathLocation? PropertyPath(JsonPathLocation? path, string name)
    => path is null ? null : new JsonPathLocation(path, name);

  internal static JsonPathLocation? ArrayPath(JsonPathLocation? path, int index)
    => path is null ? null : new JsonPathLocation(path, index);
}
