using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathMatcher
{
  private const int RecursiveIndexMarker = int.MinValue;
  private readonly record struct MatchContext(JsonNode? Node, string Path);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments)
    => FindFirstMatch(root, segments, 0);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    if (TryFindFirstMatch(root, segments, startIndex, out var firstMatch))
      return firstMatch;

    return null;
  }

  public static List<JsonNode?> FindMatches(JsonNode? root, List<JsonPathSegment> segments)
    => FindMatches(root, segments, 0);

  public static List<JsonNode?> FindMatches(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    var current = new List<JsonNode?> { root };

    for (var segmentIndex = startIndex; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
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
    => FindMatchesWithPaths(root, segments, 0, rootPath);

  public static List<JsonPathMatch> FindMatchesWithPaths(
    JsonNode? root,
    List<JsonPathSegment> segments,
    int startIndex,
    string rootPath = "$")
  {
    var current = new List<MatchContext> { new(root, rootPath) };

    for (var segmentIndex = startIndex; segmentIndex < segments.Count; segmentIndex++)
    {
      var segment = segments[segmentIndex];
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

        case JsonPathSegmentType.NestedQuery:
          CollectNestedQueryMatches(node, segment.NestedQueryBranches, results);
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

  private static bool TryFindFirstMatch(
    JsonNode? node,
    List<JsonPathSegment> segments,
    int segmentIndex,
    out JsonNode? firstMatch)
  {
    if (segmentIndex >= segments.Count)
    {
      firstMatch = node;
      return true;
    }

    var segment = segments[segmentIndex];
    JsonNode? nestedMatch = null;

    var completed = VisitSegmentMatches(node, segment, candidate =>
    {
      if (!TryFindFirstMatch(candidate, segments, segmentIndex + 1, out nestedMatch))
        return true;

      return false;
    });

    if (!completed)
    {
      firstMatch = nestedMatch;
      return true;
    }

    firstMatch = null;
    return false;
  }

  private static bool VisitSegmentMatches(JsonNode? node, JsonPathSegment segment, Func<JsonNode?, bool> visit)
  {
    switch (segment.SegmentType)
    {
      case JsonPathSegmentType.Property:
        if (node is JsonObject propertyObject &&
            propertyObject.TryGetPropertyValue(segment.PropertyName!, out var propertyValue) &&
            !visit(propertyValue))
        {
          return false;
        }

        return true;

      case JsonPathSegmentType.ArrayIndex:
        if (node is not JsonArray arrayByIndex)
          return true;

        var effectiveIndex = segment.ArrayIndex < 0 ? arrayByIndex.Count + segment.ArrayIndex : segment.ArrayIndex;
        if (effectiveIndex >= 0 && effectiveIndex < arrayByIndex.Count && !visit(arrayByIndex[effectiveIndex]))
          return false;

        return true;

      case JsonPathSegmentType.ArrayRange:
        if (node is not JsonArray arrayByRange)
          return true;

        var effectiveStart = segment.ArrayIndex < 0 ? arrayByRange.Count + segment.ArrayIndex : segment.ArrayIndex;
        var effectiveEnd = segment.ArrayRangeEnd == int.MaxValue
          ? arrayByRange.Count
          : (segment.ArrayRangeEnd < 0 ? arrayByRange.Count + segment.ArrayRangeEnd : segment.ArrayRangeEnd);

        effectiveStart = Math.Clamp(effectiveStart, 0, arrayByRange.Count);
        effectiveEnd = Math.Clamp(effectiveEnd, 0, arrayByRange.Count);

        for (var i = effectiveStart; i < effectiveEnd; i++)
        {
          if (!visit(arrayByRange[i]))
            return false;
        }

        return true;

      case JsonPathSegmentType.ArrayUnion:
        if (node is not JsonArray arrayByUnion || segment.ArrayUnionIndices is null)
          return true;

        foreach (var unionIndex in segment.ArrayUnionIndices)
        {
          var effectiveUnionIndex = unionIndex < 0 ? arrayByUnion.Count + unionIndex : unionIndex;
          if (effectiveUnionIndex >= 0 && effectiveUnionIndex < arrayByUnion.Count && !visit(arrayByUnion[effectiveUnionIndex]))
            return false;
        }

        return true;

      case JsonPathSegmentType.PropertyUnion:
        if (node is not JsonObject objectByUnion || segment.PropertyUnionNames is null)
          return true;

        foreach (var propertyName in segment.PropertyUnionNames)
        {
          if (objectByUnion.TryGetPropertyValue(propertyName, out var unionValue) && !visit(unionValue))
            return false;
        }

        return true;

      case JsonPathSegmentType.Filter:
        if (string.IsNullOrWhiteSpace(segment.FilterExpression))
          return true;

        if (node is JsonArray arrayByFilter)
        {
          foreach (var item in arrayByFilter)
          {
            if (JsonPathFilterEvaluator.Evaluate(item, segment.FilterExpression) && !visit(item))
              return false;
          }

          return true;
        }

        if (node is JsonObject objectByFilter)
        {
          foreach (var property in objectByFilter)
          {
            if (JsonPathFilterEvaluator.Evaluate(property.Value, segment.FilterExpression) && !visit(property.Value))
              return false;
          }
        }

        return true;

      case JsonPathSegmentType.ComputedIndex:
        if (node is not JsonArray arrayByComputed || string.IsNullOrWhiteSpace(segment.ComputedIndexExpression))
          return true;

        if (!JsonPathComputedExpressionEvaluator.TryEvaluateIndex(arrayByComputed.Count, segment.ComputedIndexExpression, out var computedIndex))
          return true;

        var effectiveComputedIndex = computedIndex < 0 ? arrayByComputed.Count + computedIndex : computedIndex;
        if (effectiveComputedIndex >= 0 && effectiveComputedIndex < arrayByComputed.Count && !visit(arrayByComputed[effectiveComputedIndex]))
          return false;

        return true;

      case JsonPathSegmentType.Wildcard:
        if (node is JsonArray arrayByWildcard)
        {
          foreach (var item in arrayByWildcard)
          {
            if (!visit(item))
              return false;
          }

          return true;
        }

        if (node is JsonObject objectByWildcard)
        {
          foreach (var property in objectByWildcard)
          {
            if (!visit(property.Value))
              return false;
          }
        }

        return true;

      case JsonPathSegmentType.RecursiveDescent:
        return VisitRecursiveMatches(node, segment, visit);

      case JsonPathSegmentType.FieldProjection:
        if (segment.ProjectionFields is null || segment.ProjectionFields.Length == 0)
          return true;

        if (node is JsonArray arrayByProjection)
        {
          foreach (var item in arrayByProjection)
          {
            if (!TryProjectFields(item, segment.ProjectionFields, visit))
              return false;
          }

          return true;
        }

        return TryProjectFields(node, segment.ProjectionFields, visit);

      case JsonPathSegmentType.FieldExclusion:
        if (segment.ProjectionFields is null || segment.ProjectionFields.Length == 0)
          return true;

        if (node is JsonArray arrayByExclusion)
        {
          foreach (var item in arrayByExclusion)
          {
            if (!TryExcludeFields(item, segment.ProjectionFields, visit))
              return false;
          }

          return true;
        }

        return TryExcludeFields(node, segment.ProjectionFields, visit);

      case JsonPathSegmentType.FieldExistence:
        if (string.IsNullOrWhiteSpace(segment.PropertyName))
          return true;

        if (segment.PropertyName == "*")
        {
          var any = node switch
          {
            JsonObject obj => obj.Count > 0,
            JsonArray array => array.Count > 0,
            _ => false
          };

          return visit(JsonValue.Create(any));
        }

        var exists = node switch
        {
          JsonObject obj => obj.ContainsKey(segment.PropertyName),
          JsonArray array => AnyItemContainsField(array, segment.PropertyName),
          _ => false
        };

        return visit(JsonValue.Create(exists));

      case JsonPathSegmentType.FieldCount:
        if (string.IsNullOrWhiteSpace(segment.PropertyName))
          return true;

        if (segment.PropertyName == "*")
        {
          var anyCount = node switch
          {
            JsonObject obj => obj.Count,
            JsonArray array => array.Count,
            _ => 0
          };

          return visit(JsonValue.Create(anyCount));
        }

        var count = node switch
        {
          JsonObject obj => obj.ContainsKey(segment.PropertyName) ? 1 : 0,
          JsonArray array => CountItemsWithField(array, segment.PropertyName),
          _ => 0
        };

        return visit(JsonValue.Create(count));

      case JsonPathSegmentType.NestedQuery:
        if (node is not JsonObject nestedObj || segment.NestedQueryBranches is null)
          return true;

        var nestedMatch = BuildNestedQueryResult(nestedObj, segment.NestedQueryBranches);
        return nestedMatch is null || visit(nestedMatch);

      default:
        return true;
    }
  }

  private static bool VisitRecursiveMatches(JsonNode? node, JsonPathSegment segment, Func<JsonNode?, bool> visit)
  {
    if (node is JsonObject obj)
    {
      foreach (var property in obj)
      {
        if (segment.PropertyName == "*" ||
            (segment.PropertyName != null && property.Key == segment.PropertyName))
        {
          if (!visit(property.Value))
            return false;
        }

        if (property.Value is not null && !VisitRecursiveMatches(property.Value, segment, visit))
          return false;
      }

      return true;
    }

    if (node is not JsonArray array)
      return true;

    if (segment.PropertyName == null)
    {
      if (segment.ArrayRangeEnd == RecursiveIndexMarker)
      {
        var recursiveIndex = segment.ArrayIndex < 0 ? array.Count + segment.ArrayIndex : segment.ArrayIndex;
        if (recursiveIndex >= 0 && recursiveIndex < array.Count && !visit(array[recursiveIndex]))
          return false;
      }
      else
      {
        var recursiveStart = segment.ArrayIndex < 0 ? array.Count + segment.ArrayIndex : segment.ArrayIndex;
        var recursiveEnd = segment.ArrayRangeEnd == int.MaxValue
          ? array.Count
          : (segment.ArrayRangeEnd < 0 ? array.Count + segment.ArrayRangeEnd : segment.ArrayRangeEnd);

        recursiveStart = Math.Clamp(recursiveStart, 0, array.Count);
        recursiveEnd = Math.Clamp(recursiveEnd, 0, array.Count);

        for (var i = recursiveStart; i < recursiveEnd; i++)
        {
          if (!visit(array[i]))
            return false;
        }
      }
    }

    foreach (var item in array)
    {
      if (item is not null && !VisitRecursiveMatches(item, segment, visit))
        return false;
    }

    return true;
  }

  private static bool TryProjectFields(JsonNode? node, string[] fields, Func<JsonNode?, bool> visit)
  {
    if (node is not JsonObject obj)
      return true;

    var projectedObject = new JsonObject();

    foreach (var field in fields)
    {
      if (obj.TryGetPropertyValue(field, out var value))
        projectedObject[field] = value?.DeepClone();
    }

    return visit(projectedObject);
  }

  private static bool TryExcludeFields(JsonNode? node, string[] fields, Func<JsonNode?, bool> visit)
  {
    if (node is not JsonObject obj)
      return true;

    var excludedObject = new JsonObject();
    var excludeSet = new HashSet<string>(fields);

    foreach (var prop in obj)
    {
      if (!excludeSet.Contains(prop.Key))
        excludedObject[prop.Key] = prop.Value?.DeepClone();
    }

    return visit(excludedObject);
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

        case JsonPathSegmentType.NestedQuery:
          CollectNestedQueryMatchesWithPaths(context, segment.NestedQueryBranches, results);
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

  private static void CollectNestedQueryMatches(JsonNode? node, NestedQueryBranch[]? branches, List<JsonNode?> results)
  {
    var resultObject = BuildNestedQueryResult(node, branches);
    if (resultObject is not null)
      results.Add(resultObject);
  }

  private static void CollectNestedQueryMatchesWithPaths(MatchContext context, NestedQueryBranch[]? branches, List<MatchContext> results)
  {
    var resultObject = BuildNestedQueryResult(context.Node, branches);
    if (resultObject is not null)
      results.Add(new MatchContext(resultObject, context.Path));
  }

  private static JsonObject? BuildNestedQueryResult(JsonNode? node, NestedQueryBranch[]? branches)
  {
    if (node is not JsonObject obj || branches is null || branches.Length == 0)
      return null;

    var resultObject = new JsonObject();

    foreach (var branch in branches)
    {
      if (!obj.TryGetPropertyValue(branch.PropertyName, out var value) || value is null)
        continue;

      if (branch.SubSegments.Count == 0)
      {
        // No sub-path — include value as-is
        resultObject[branch.PropertyName] = value.DeepClone();
      }
      else
      {
        // Apply sub-segments to the property value
        var subMatches = FindMatches(value, branch.SubSegments, 0);

        if (subMatches.Count == 0)
          continue; // Nothing matched — omit key

        if (subMatches.Count == 1)
        {
          resultObject[branch.PropertyName] = subMatches[0]?.DeepClone();
        }
        else
        {
          var resultArray = new JsonArray();
          foreach (var match in subMatches)
            resultArray.Add(match?.DeepClone());
          resultObject[branch.PropertyName] = resultArray;
        }
      }
    }

    return resultObject.Count > 0 ? resultObject : null;
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

