using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathMatcher
{
  private const int RecursiveIndexMarker = int.MinValue;
  private readonly record struct MatchContext(JsonNode? Node, string Path);

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

  public static List<JsonPathMatch> FindMatchesWithPaths(
    JsonNode? root,
    List<JsonPathSegment> segments,
    string rootPath = "$")
  {
    var current = new List<MatchContext> { new(root, rootPath) };

    foreach (var segment in segments)
    {
      current = FindSegmentMatchesWithPaths(current, segment);
      if (current.Count == 0)
        break;
    }

    var results = new List<JsonPathMatch>(current.Count);
    foreach (var context in current)
      results.Add(new JsonPathMatch(context.Path, context.Node));

    return results;
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

        case JsonPathSegmentType.FieldExclusion:
          CollectFieldExclusionMatches(node, segment.ProjectionFields, results);
          break;

        case JsonPathSegmentType.FieldExistence:
          CollectFieldExistenceMatches(node, segment.PropertyName, results);
          break;

        case JsonPathSegmentType.FieldCount:
          CollectFieldCountMatches(node, segment.PropertyName, results);
          break;
      }
    }

    return results;
  }

  private static List<MatchContext> FindSegmentMatchesWithPaths(
    IEnumerable<MatchContext> current,
    JsonPathSegment segment)
  {
    var results = new List<MatchContext>();

    foreach (var context in current)
    {
      switch (segment.SegmentType)
      {
        case JsonPathSegmentType.Property:
          CollectPropertyMatchesWithPaths(context, segment.PropertyName!, results);
          break;

        case JsonPathSegmentType.ArrayIndex:
          CollectArrayIndexMatchWithPaths(context, segment.ArrayIndex, results);
          break;

        case JsonPathSegmentType.ArrayRange:
          CollectArrayRangeMatchesWithPaths(context, segment.ArrayIndex, segment.ArrayRangeEnd, results);
          break;

        case JsonPathSegmentType.ArrayUnion:
          CollectArrayUnionMatchesWithPaths(context, segment.ArrayUnionIndices, results);
          break;

        case JsonPathSegmentType.PropertyUnion:
          CollectPropertyUnionMatchesWithPaths(context, segment.PropertyUnionNames, results);
          break;

        case JsonPathSegmentType.Filter:
          CollectFilterMatchesWithPaths(context, segment.FilterExpression, results);
          break;

        case JsonPathSegmentType.ComputedIndex:
          CollectComputedIndexMatchWithPaths(context, segment.ComputedIndexExpression, results);
          break;

        case JsonPathSegmentType.Wildcard:
          CollectWildcardMatchesWithPaths(context, results);
          break;

        case JsonPathSegmentType.RecursiveDescent:
          CollectRecursiveMatchesWithPaths(context, segment, results);
          break;

        case JsonPathSegmentType.FieldProjection:
          CollectFieldProjectionMatchesWithPaths(context, segment.ProjectionFields, results);
          break;

        case JsonPathSegmentType.FieldExclusion:
          CollectFieldExclusionMatchesWithPaths(context, segment.ProjectionFields, results);
          break;

        case JsonPathSegmentType.FieldExistence:
          CollectFieldExistenceMatchesWithPaths(context, segment.PropertyName, results);
          break;

        case JsonPathSegmentType.FieldCount:
          CollectFieldCountMatchesWithPaths(context, segment.PropertyName, results);
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

  private static void CollectFieldExclusionMatches(JsonNode? node, string[]? fields, List<JsonNode?> results)
  {
    if (fields == null || fields.Length == 0)
      return;

    if (node is JsonArray array)
    {
      foreach (var item in array)
      {
        ExcludeFields(item, fields, results);
      }
      return;
    }

    ExcludeFields(node, fields, results);
  }

  private static void ExcludeFields(JsonNode? node, string[] fields, List<JsonNode?> results)
  {
    if (node is not JsonObject obj)
      return;

    var excludedObject = new JsonObject();
    var excludeSet = new HashSet<string>(fields);

    foreach (var prop in obj)
    {
      if (!excludeSet.Contains(prop.Key))
      {
        excludedObject[prop.Key] = prop.Value?.DeepClone();
      }
    }

    results.Add(excludedObject);
  }

  private static void CollectFieldExistenceMatches(JsonNode? node, string? fieldName, List<JsonNode?> results)
  {
    if (string.IsNullOrWhiteSpace(fieldName))
      return;

    if (fieldName == "*")
    {
      bool any = node switch
      {
        JsonObject obj => obj.Count > 0,
        JsonArray array => array.Count > 0,
        _ => false
      };

      results.Add(JsonValue.Create(any));
      return;
    }

    bool exists = node switch
    {
      JsonObject obj => obj.ContainsKey(fieldName),
      JsonArray array => AnyItemContainsField(array, fieldName),
      _ => false
    };

    results.Add(JsonValue.Create(exists));
  }

  private static void CollectFieldCountMatches(JsonNode? node, string? fieldName, List<JsonNode?> results)
  {
    if (string.IsNullOrWhiteSpace(fieldName))
      return;

    if (fieldName == "*")
    {
      int anyCount = node switch
      {
        JsonObject obj => obj.Count,
        JsonArray array => array.Count,
        _ => 0
      };

      results.Add(JsonValue.Create(anyCount));
      return;
    }

    int count = node switch
    {
      JsonObject obj => obj.ContainsKey(fieldName) ? 1 : 0,
      JsonArray array => CountItemsWithField(array, fieldName),
      _ => 0
    };

    results.Add(JsonValue.Create(count));
  }

  private static bool AnyItemContainsField(JsonArray array, string fieldName)
  {
    foreach (var item in array)
    {
      if (item is JsonObject obj && obj.ContainsKey(fieldName))
        return true;
    }

    return false;
  }

  private static int CountItemsWithField(JsonArray array, string fieldName)
  {
    var count = 0;
    foreach (var item in array)
    {
      if (item is JsonObject obj && obj.ContainsKey(fieldName))
        count++;
    }

    return count;
  }

  private static void CollectPropertyMatchesWithPaths(MatchContext context, string propertyName, List<MatchContext> results)
  {
    if (context.Node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out var value))
      results.Add(new MatchContext(value, AppendPropertyPath(context.Path, propertyName)));
  }

  private static void CollectArrayIndexMatchWithPaths(MatchContext context, int index, List<MatchContext> results)
  {
    if (context.Node is not JsonArray array)
      return;

    var effectiveIndex = index < 0 ? array.Count + index : index;
    if (effectiveIndex >= 0 && effectiveIndex < array.Count)
      results.Add(new MatchContext(array[effectiveIndex], AppendArrayPath(context.Path, effectiveIndex)));
  }

  private static void CollectArrayRangeMatchesWithPaths(MatchContext context, int start, int endExclusive, List<MatchContext> results)
  {
    if (context.Node is not JsonArray array)
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
      results.Add(new MatchContext(array[i], AppendArrayPath(context.Path, i)));
  }

  private static void CollectWildcardMatchesWithPaths(MatchContext context, List<MatchContext> results)
  {
    if (context.Node is JsonArray array)
    {
      for (var i = 0; i < array.Count; i++)
        results.Add(new MatchContext(array[i], AppendArrayPath(context.Path, i)));
      return;
    }

    if (context.Node is JsonObject obj)
    {
      foreach (var property in obj)
        results.Add(new MatchContext(property.Value, AppendPropertyPath(context.Path, property.Key)));
    }
  }

  private static void CollectArrayUnionMatchesWithPaths(MatchContext context, int[]? indices, List<MatchContext> results)
  {
    if (context.Node is not JsonArray || indices is null)
      return;

    foreach (var index in indices)
      CollectArrayIndexMatchWithPaths(context, index, results);
  }

  private static void CollectPropertyUnionMatchesWithPaths(MatchContext context, string[]? propertyNames, List<MatchContext> results)
  {
    if (context.Node is not JsonObject obj || propertyNames is null)
      return;

    foreach (var propertyName in propertyNames)
    {
      if (obj.TryGetPropertyValue(propertyName, out var value))
      {
        results.Add(new MatchContext(value, AppendPropertyPath(context.Path, propertyName)));
      }
    }
  }

  private static void CollectFilterMatchesWithPaths(MatchContext context, string? filterExpression, List<MatchContext> results)
  {
    if (string.IsNullOrWhiteSpace(filterExpression))
      return;

    if (context.Node is JsonArray array)
    {
      for (var i = 0; i < array.Count; i++)
      {
        var item = array[i];
        if (JsonPathFilterEvaluator.Evaluate(item, filterExpression))
          results.Add(new MatchContext(item, AppendArrayPath(context.Path, i)));
      }

      return;
    }

    if (context.Node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        if (JsonPathFilterEvaluator.Evaluate(property.Value, filterExpression))
          results.Add(new MatchContext(property.Value, AppendPropertyPath(context.Path, property.Key)));
      }
    }
  }

  private static void CollectComputedIndexMatchWithPaths(MatchContext context, string? expression, List<MatchContext> results)
  {
    if (context.Node is not JsonArray array || string.IsNullOrWhiteSpace(expression))
      return;

    if (!JsonPathComputedExpressionEvaluator.TryEvaluateIndex(array.Count, expression, out var index))
      return;

    CollectArrayIndexMatchWithPaths(context, index, results);
  }

  private static void CollectRecursiveMatchesWithPaths(MatchContext context, JsonPathSegment segment, List<MatchContext> results)
  {
    if (context.Node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        var propertyPath = AppendPropertyPath(context.Path, property.Key);

        if (segment.PropertyName == "*" ||
            (segment.PropertyName != null && property.Key == segment.PropertyName))
        {
          results.Add(new MatchContext(property.Value, propertyPath));
        }

        if (property.Value != null)
          CollectRecursiveMatchesWithPaths(new MatchContext(property.Value, propertyPath), segment, results);
      }

      return;
    }

    if (context.Node is not JsonArray array)
      return;

    if (segment.PropertyName == null)
    {
      if (segment.ArrayRangeEnd == RecursiveIndexMarker)
        CollectArrayIndexMatchWithPaths(context, segment.ArrayIndex, results);
      else
        CollectArrayRangeMatchesWithPaths(context, segment.ArrayIndex, segment.ArrayRangeEnd, results);
    }

    for (var i = 0; i < array.Count; i++)
    {
      var item = array[i];
      if (item != null)
        CollectRecursiveMatchesWithPaths(new MatchContext(item, AppendArrayPath(context.Path, i)), segment, results);
    }
  }

  private static void CollectFieldProjectionMatchesWithPaths(MatchContext context, string[]? fields, List<MatchContext> results)
  {
    if (fields == null || fields.Length == 0)
      return;

    if (context.Node is JsonArray array)
    {
      for (var i = 0; i < array.Count; i++)
      {
        var item = array[i];
        var projected = BuildProjectedObject(item, fields);
        if (projected is not null)
          results.Add(new MatchContext(projected, AppendArrayPath(context.Path, i)));
      }

      return;
    }

    var projectedObject = BuildProjectedObject(context.Node, fields);
    if (projectedObject is not null)
      results.Add(new MatchContext(projectedObject, context.Path));
  }

  private static JsonObject? BuildProjectedObject(JsonNode? node, string[] fields)
  {
    if (node is not JsonObject obj)
      return null;

    var projectedObject = new JsonObject();

    foreach (var field in fields)
    {
      if (obj.TryGetPropertyValue(field, out var value))
      {
        projectedObject[field] = value?.DeepClone();
      }
    }

    return projectedObject;
  }

  private static void CollectFieldExclusionMatchesWithPaths(MatchContext context, string[]? fields, List<MatchContext> results)
  {
    if (fields == null || fields.Length == 0)
      return;

    if (context.Node is JsonArray array)
    {
      for (var i = 0; i < array.Count; i++)
      {
        var item = array[i];
        var excluded = BuildExcludedObject(item, fields);
        if (excluded is not null)
          results.Add(new MatchContext(excluded, AppendArrayPath(context.Path, i)));
      }

      return;
    }

    var excludedObject = BuildExcludedObject(context.Node, fields);
    if (excludedObject is not null)
      results.Add(new MatchContext(excludedObject, context.Path));
  }

  private static JsonObject? BuildExcludedObject(JsonNode? node, string[] fields)
  {
    if (node is not JsonObject obj)
      return null;

    var excludedObject = new JsonObject();
    var excludeSet = new HashSet<string>(fields);

    foreach (var prop in obj)
    {
      if (!excludeSet.Contains(prop.Key))
      {
        excludedObject[prop.Key] = prop.Value?.DeepClone();
      }
    }

    return excludedObject;
  }

  private static void CollectFieldExistenceMatchesWithPaths(MatchContext context, string? fieldName, List<MatchContext> results)
  {
    if (string.IsNullOrWhiteSpace(fieldName))
      return;

    if (fieldName == "*")
    {
      bool any = context.Node switch
      {
        JsonObject obj => obj.Count > 0,
        JsonArray array => array.Count > 0,
        _ => false
      };

      results.Add(new MatchContext(JsonValue.Create(any), context.Path));
      return;
    }

    bool exists = context.Node switch
    {
      JsonObject obj => obj.ContainsKey(fieldName),
      JsonArray array => AnyItemContainsField(array, fieldName),
      _ => false
    };

    results.Add(new MatchContext(JsonValue.Create(exists), context.Path));
  }

  private static void CollectFieldCountMatchesWithPaths(MatchContext context, string? fieldName, List<MatchContext> results)
  {
    if (string.IsNullOrWhiteSpace(fieldName))
      return;

    if (fieldName == "*")
    {
      int anyCount = context.Node switch
      {
        JsonObject obj => obj.Count,
        JsonArray array => array.Count,
        _ => 0
      };

      results.Add(new MatchContext(JsonValue.Create(anyCount), context.Path));
      return;
    }

    int count = context.Node switch
    {
      JsonObject obj => obj.ContainsKey(fieldName) ? 1 : 0,
      JsonArray array => CountItemsWithField(array, fieldName),
      _ => 0
    };

    results.Add(new MatchContext(JsonValue.Create(count), context.Path));
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

  private static string AppendArrayPath(string parentPath, int index)
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

