using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathMatcher
{
  private const int RecursiveIndexMarker = int.MinValue;
  private readonly record struct MatchContext(JsonNode? Node, string? Path);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments)
    => FindFirstMatch(root, segments, 0);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    foreach (var match in EnumerateMatches(root, segments, startIndex))
      return match;
    return null;
  }

  public static List<JsonNode?> FindMatches(JsonNode? root, List<JsonPathSegment> segments)
    => FindMatches(root, segments, 0);

  public static List<JsonNode?> FindMatches(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
    => new(EnumerateMatches(root, segments, startIndex));

  public static List<JsonPathMatch> FindMatchesWithPaths(
    JsonNode? root, List<JsonPathSegment> segments, string rootPath = "$")
    => FindMatchesWithPaths(root, segments, 0, rootPath);

  public static List<JsonPathMatch> FindMatchesWithPaths(
    JsonNode? root, List<JsonPathSegment> segments, int startIndex, string rootPath = "$")
    => new(EnumerateMatchesWithPaths(root, segments, startIndex, rootPath));

  public static IEnumerable<JsonNode?> EnumerateMatches(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    foreach (var match in Traverse(new MatchContext(root, null), segments, startIndex))
      yield return match.Node;
  }

  public static IEnumerable<JsonPathMatch> EnumerateMatchesWithPaths(
    JsonNode? root, List<JsonPathSegment> segments, int startIndex, string rootPath)
  {
    foreach (var match in Traverse(new MatchContext(root, rootPath), segments, startIndex))
      yield return new JsonPathMatch(match.Path!, match.Node);
  }

  private static IEnumerable<MatchContext> Traverse(MatchContext current, List<JsonPathSegment> segments, int index)
  {
    if (!AdvanceSingleMatches(ref current, segments, ref index))
      yield break;

    if (index >= segments.Count)
    {
      yield return current;
      yield break;
    }

    foreach (var child in Select(current, segments[index]))
    {
      var candidate = child;
      var nextIndex = index + 1;
      if (!AdvanceSingleMatches(ref candidate, segments, ref nextIndex))
        continue;
      if (nextIndex >= segments.Count)
      {
        yield return candidate;
        continue;
      }
      foreach (var match in Traverse(candidate, segments, nextIndex))
        yield return match;
    }
  }

  private static bool AdvanceSingleMatches(ref MatchContext current, List<JsonPathSegment> segments, ref int index)
  {
    while (index < segments.Count)
    {
      var segment = segments[index];
      if (segment.SegmentType == JsonPathSegmentType.Property)
      {
        if (current.Node is not JsonObject obj || !obj.TryGetPropertyValue(segment.PropertyName!, out var property))
          return false;
        current = Property(current, property, segment.PropertyName!);
      }
      else if (segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ComputedIndex)
      {
        if (current.Node is not JsonArray array)
          return false;

        var arrayIndex = segment.ArrayIndex;
        if (segment.SegmentType == JsonPathSegmentType.ComputedIndex
            && !JsonPathComputedExpressionEvaluator.TryEvaluateIndex(array.Count,
              segment.ComputedIndexExpression ?? string.Empty, out arrayIndex))
          return false;
        arrayIndex = arrayIndex < 0 ? array.Count + arrayIndex : arrayIndex;
        if (arrayIndex < 0 || arrayIndex >= array.Count)
          return false;
        current = ArrayItem(current, array[arrayIndex], arrayIndex);
      }
      else
      {
        break;
      }
      index++;
    }
    return true;
  }

  private static IEnumerable<MatchContext> Select(MatchContext current, JsonPathSegment segment)
  {
    var node = current.Node;
    switch (segment.SegmentType)
    {
      case JsonPathSegmentType.ArrayRange:
        foreach (var match in SelectRange(current, segment.ArrayIndex, segment.ArrayRangeEnd))
          yield return match;
        yield break;

      case JsonPathSegmentType.ArrayUnion:
        if (node is JsonArray unionArray && segment.ArrayUnionIndices is not null)
        {
          foreach (var unionIndex in segment.ArrayUnionIndices)
          {
            var effective = unionIndex < 0 ? unionArray.Count + unionIndex : unionIndex;
            if (effective >= 0 && effective < unionArray.Count)
              yield return ArrayItem(current, unionArray[effective], effective);
          }
        }
        yield break;

      case JsonPathSegmentType.PropertyUnion:
        if (node is JsonObject unionObject && segment.PropertyUnionNames is not null)
        {
          foreach (var name in segment.PropertyUnionNames)
          {
            if (unionObject.TryGetPropertyValue(name, out var value))
              yield return Property(current, value, name);
          }
        }
        yield break;

      case JsonPathSegmentType.Wildcard:
      case JsonPathSegmentType.Filter:
        var filter = segment.SegmentType == JsonPathSegmentType.Filter
          ? segment.FilterNode ?? JsonPathFilterNode.Compile(segment.FilterExpression ?? string.Empty)
          : null;
        foreach (var child in Children(current))
        {
          if (filter is null || filter.Evaluate(child.Node))
            yield return child;
        }
        yield break;

      case JsonPathSegmentType.RecursiveDescent:
        foreach (var match in SelectRecursive(current, segment))
          yield return match;
        yield break;

      case JsonPathSegmentType.FieldProjection:
      case JsonPathSegmentType.FieldExclusion:
        if (segment.ProjectionFields is not { Length: > 0 } fields)
          yield break;
        if (node is JsonArray)
        {
          foreach (var child in Children(current))
          {
            var projected = Project(child.Node, fields, segment.SegmentType == JsonPathSegmentType.FieldExclusion);
            if (projected is not null)
              yield return new MatchContext(projected, child.Path);
          }
        }
        else
        {
          var projected = Project(node, fields, segment.SegmentType == JsonPathSegmentType.FieldExclusion);
          if (projected is not null)
            yield return new MatchContext(projected, current.Path);
        }
        yield break;

      case JsonPathSegmentType.FieldExistence:
      case JsonPathSegmentType.FieldCount:
        if (string.IsNullOrWhiteSpace(segment.PropertyName))
          yield break;
        var count = CountFields(node, segment.PropertyName,
          stopAfterFirst: segment.SegmentType == JsonPathSegmentType.FieldExistence);
        yield return new MatchContext(segment.SegmentType == JsonPathSegmentType.FieldExistence
          ? JsonValue.Create(count > 0) : JsonValue.Create(count), current.Path);
        yield break;

      case JsonPathSegmentType.NestedQuery:
        var nested = BuildNestedQueryResult(node, segment.NestedQueryBranches);
        if (nested is not null)
          yield return new MatchContext(nested, current.Path);
        yield break;

      case JsonPathSegmentType.Schema:
        var schema = JsonPathSchemaGenerator.GenerateSchema(node);
        if (schema is not null)
          yield return new MatchContext(schema, current.Path);
        yield break;
    }
  }

  private static IEnumerable<MatchContext> Children(MatchContext current)
  {
    if (current.Node is JsonArray array)
    {
      for (var i = 0; i < array.Count; i++)
        yield return ArrayItem(current, array[i], i);
    }
    else if (current.Node is JsonObject obj)
    {
      foreach (var property in obj)
        yield return Property(current, property.Value, property.Key);
    }
  }

  private static IEnumerable<MatchContext> SelectRange(MatchContext current, int start, int end)
  {
    if (current.Node is not JsonArray array)
      yield break;

    start = Math.Clamp(start < 0 ? array.Count + start : start, 0, array.Count);
    end = end == int.MaxValue ? array.Count : Math.Clamp(end < 0 ? array.Count + end : end, 0, array.Count);
    for (var i = start; i < end; i++)
      yield return ArrayItem(current, array[i], i);
  }

  private static IEnumerable<MatchContext> SelectRecursive(MatchContext current, JsonPathSegment segment)
  {
    if (current.Node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        var child = Property(current, property.Value, property.Key);
        if (segment.PropertyName == "*" || property.Key == segment.PropertyName)
          yield return child;
        foreach (var descendant in SelectRecursive(child, segment))
          yield return descendant;
      }
    }
    else if (current.Node is JsonArray array)
    {
      if (segment.PropertyName is null)
      {
        if (segment.ArrayRangeEnd == RecursiveIndexMarker)
        {
          var index = segment.ArrayIndex < 0 ? array.Count + segment.ArrayIndex : segment.ArrayIndex;
          if (index >= 0 && index < array.Count)
            yield return ArrayItem(current, array[index], index);
        }
        else
        {
          foreach (var match in SelectRange(current, segment.ArrayIndex, segment.ArrayRangeEnd))
            yield return match;
        }
      }
      foreach (var child in Children(current))
      {
        foreach (var descendant in SelectRecursive(child, segment))
          yield return descendant;
      }
    }
  }

  private static JsonObject? Project(JsonNode? node, string[] fields, bool exclude)
  {
    if (node is not JsonObject obj)
      return null;

    var result = new JsonObject();
    if (exclude)
    {
      foreach (var property in obj)
      {
        if (Array.IndexOf(fields, property.Key) < 0)
          result[property.Key] = property.Value?.DeepClone();
      }
    }
    else
    {
      foreach (var field in fields)
      {
        if (obj.TryGetPropertyValue(field, out var value))
          result[field] = value?.DeepClone();
      }
    }
    return exclude || result.Count > 0 ? result : null;
  }

  private static int CountFields(JsonNode? node, string name, bool stopAfterFirst)
  {
    if (name == "*")
      return node switch { JsonObject obj => obj.Count, JsonArray array => array.Count, _ => 0 };
    if (node is JsonObject single)
      return single.ContainsKey(name) ? 1 : 0;
    if (node is not JsonArray items)
      return 0;

    var count = 0;
    foreach (var item in items)
    {
      if (item is JsonObject obj && obj.ContainsKey(name))
      {
        count++;
        if (stopAfterFirst)
          break;
      }
    }
    return count;
  }

  private static JsonObject? BuildNestedQueryResult(JsonNode? node, NestedQueryBranch[]? branches)
  {
    if (node is not JsonObject obj || branches is not { Length: > 0 })
      return null;

    var result = new JsonObject();
    foreach (var branch in branches)
    {
      if (!obj.TryGetPropertyValue(branch.PropertyName, out var value) || value is null)
        continue;
      using var matches = EnumerateMatches(value, branch.SubSegments, 0).GetEnumerator();
      if (!matches.MoveNext())
        continue;
      var first = matches.Current?.DeepClone();
      if (!matches.MoveNext())
      {
        result[branch.PropertyName] = first;
        continue;
      }
      var array = new JsonArray(first);
      do
      {
        array.Add(matches.Current?.DeepClone());
      } while (matches.MoveNext());
      result[branch.PropertyName] = array;
    }
    return result.Count > 0 ? result : null;
  }

  private static MatchContext Property(MatchContext parent, JsonNode? value, string name)
    => new(value, parent.Path is null ? null : AppendPropertyPath(parent.Path, name));

  private static MatchContext ArrayItem(MatchContext parent, JsonNode? value, int index)
    => new(value, parent.Path is null ? null : $"{parent.Path}[{index}]");

  internal static string AppendPropertyPath(string parentPath, string propertyName)
    => JsonPathLocation.AppendProperty(parentPath, propertyName);

  internal static bool IsSimpleIdentifier(string name)
  {
    if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
      return false;
    for (var i = 1; i < name.Length; i++)
    {
      if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
        return false;
    }
    return true;
  }
}
