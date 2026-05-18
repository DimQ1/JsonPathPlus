using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathMatcher
{
  private const int RecursiveIndexMarker = int.MinValue;

  public static List<JsonNode?> FindMatches(JsonNode? root, List<JsonPathSegment> segments)
  {
    var current = new List<JsonNode?> { root };

    foreach (var segment in segments)
    {
      current = FindSegmentMatches(current, segment);
      if (current.Count == 0)
        break;
    }

    return current;
  }

  private static List<JsonNode?> FindSegmentMatches(
    IEnumerable<JsonNode?> current,
    JsonPathSegment segment)
  {
    var results = new List<JsonNode?>();

    foreach (var node in current)
    {
      switch (segment.SegmentType)
      {
        case JsonPathSegmentType.Property:
          CollectPropertyMatches(node, segment.PropertyName!, results);
          break;

        case JsonPathSegmentType.ArrayIndex:
          CollectArrayIndexMatch(node, segment.ArrayIndex, results);
          break;

        case JsonPathSegmentType.ArrayRange:
          CollectArrayRangeMatches(node, segment.ArrayIndex, segment.ArrayRangeEnd, results);
          break;

        case JsonPathSegmentType.ArrayUnion:
          CollectArrayUnionMatches(node, segment.ArrayUnionIndices, results);
          break;

        case JsonPathSegmentType.PropertyUnion:
          CollectPropertyUnionMatches(node, segment.PropertyUnionNames, results);
          break;

        case JsonPathSegmentType.Filter:
          CollectFilterMatches(node, segment.FilterExpression, results);
          break;

        case JsonPathSegmentType.ComputedIndex:
          CollectComputedIndexMatch(node, segment.ComputedIndexExpression, results);
          break;

        case JsonPathSegmentType.Wildcard:
          CollectWildcardMatches(node, results);
          break;

        case JsonPathSegmentType.RecursiveDescent:
          CollectRecursiveMatches(node, segment, results);
          break;

        case JsonPathSegmentType.FieldProjection:
          CollectFieldProjectionMatches(node, segment.ProjectionFields, results);
          break;
      }
    }

    return results;
  }

  private static void CollectPropertyMatches(JsonNode? node, string propertyName, List<JsonNode?> results)
  {
    if (node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out var value))
      results.Add(value);
  }

  private static void CollectArrayIndexMatch(JsonNode? node, int index, List<JsonNode?> results)
  {
    if (node is not JsonArray array)
      return;

    var effectiveIndex = index < 0 ? array.Count + index : index;
    if (effectiveIndex >= 0 && effectiveIndex < array.Count)
      results.Add(array[effectiveIndex]);
  }

  private static void CollectArrayRangeMatches(JsonNode? node, int start, int endExclusive, List<JsonNode?> results)
  {
    if (node is not JsonArray array)
      return;

    var effectiveStart = start < 0 ? array.Count + start : start;
    var effectiveEnd = endExclusive == int.MaxValue
      ? array.Count
      : (endExclusive < 0 ? array.Count + endExclusive : endExclusive);

    effectiveStart = Math.Clamp(effectiveStart, 0, array.Count);
    effectiveEnd = Math.Clamp(effectiveEnd, 0, array.Count);

    if (effectiveEnd < effectiveStart)
      return;

    for (int i = effectiveStart; i < effectiveEnd; i++)
      results.Add(array[i]);
  }

  private static void CollectWildcardMatches(JsonNode? node, List<JsonNode?> results)
  {
    if (node is JsonArray array)
    {
      foreach (var item in array)
        results.Add(item);
      return;
    }

    if (node is JsonObject obj)
    {
      foreach (var property in obj)
        results.Add(property.Value);
    }
  }

  private static void CollectArrayUnionMatches(JsonNode? node, int[]? indices, List<JsonNode?> results)
  {
    if (node is not JsonArray array || indices is null)
      return;

    foreach (var index in indices)
      CollectArrayIndexMatch(array, index, results);
  }

  private static void CollectPropertyUnionMatches(JsonNode? node, string[]? propertyNames, List<JsonNode?> results)
  {
    if (node is not JsonObject obj || propertyNames is null)
      return;

    foreach (var propertyName in propertyNames)
    {
      if (obj.TryGetPropertyValue(propertyName, out var value))
        results.Add(value);
    }
  }

  private static void CollectFilterMatches(JsonNode? node, string? filterExpression, List<JsonNode?> results)
  {
    if (string.IsNullOrWhiteSpace(filterExpression))
      return;

    if (node is JsonArray array)
    {
      foreach (var item in array)
      {
        if (JsonPathFilterEvaluator.Evaluate(item, filterExpression))
          results.Add(item);
      }

      return;
    }

    if (node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        if (JsonPathFilterEvaluator.Evaluate(property.Value, filterExpression))
          results.Add(property.Value);
      }
    }
  }

  private static void CollectComputedIndexMatch(JsonNode? node, string? expression, List<JsonNode?> results)
  {
    if (node is not JsonArray array || string.IsNullOrWhiteSpace(expression))
      return;

    if (!JsonPathComputedExpressionEvaluator.TryEvaluateIndex(array.Count, expression, out var index))
      return;

    CollectArrayIndexMatch(array, index, results);
  }

  private static void CollectRecursiveMatches(JsonNode? node, JsonPathSegment segment, List<JsonNode?> results)
  {
    if (node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        if (segment.PropertyName == "*" ||
            (segment.PropertyName != null && property.Key == segment.PropertyName))
        {
          results.Add(property.Value);
        }

        if (property.Value != null)
          CollectRecursiveMatches(property.Value, segment, results);
      }

      return;
    }

    if (node is not JsonArray array)
      return;

    if (segment.PropertyName == null)
    {
      if (segment.ArrayRangeEnd == RecursiveIndexMarker)
        CollectArrayIndexMatch(array, segment.ArrayIndex, results);
      else
        CollectArrayRangeMatches(array, segment.ArrayIndex, segment.ArrayRangeEnd, results);
    }

    foreach (var item in array)
    {
      if (item != null)
        CollectRecursiveMatches(item, segment, results);
    }
  }

  private static void CollectFieldProjectionMatches(JsonNode? node, string[]? fields, List<JsonNode?> results)
  {
    if (fields == null || fields.Length == 0)
      return;

    if (node is JsonArray array)
    {
      foreach (var item in array)
      {
        ProjectFields(item, fields, results);
      }
      return;
    }

    ProjectFields(node, fields, results);
  }

  private static void ProjectFields(JsonNode? node, string[] fields, List<JsonNode?> results)
  {
    if (node is not JsonObject obj)
      return;

    var projectedObject = new JsonObject();

    foreach (var field in fields)
    {
      if (obj.TryGetPropertyValue(field, out var value))
      {
        projectedObject[field] = value?.DeepClone();
      }
    }

    results.Add(projectedObject);
  }
}

