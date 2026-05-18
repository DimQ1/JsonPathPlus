using System;
using System.Collections.Generic;
using System.Linq;

namespace JsonPathPlus;

internal static class JsonPathParser
{
  private const int RecursiveIndexMarker = int.MinValue;

  public static List<JsonPathSegment> Parse(ReadOnlySpan<char> path)
  {
    if (path.Length > 0 && path[0] == '$')
      path = path[1..];

    var segments = new List<JsonPathSegment>();

    while (path.Length > 0)
    {
      if (TryParseRecursiveSegment(ref path, segments))
        continue;

      if (path[0] == '.')
      {
        path = path[1..];
        continue;
      }

      if (path[0] == '[')
      {
        ParseBracketSegment(ref path, segments);
        continue;
      }

      ParsePropertySegment(ref path, segments);
    }

    return segments;
  }

  private static bool TryParseRecursiveSegment(
    ref ReadOnlySpan<char> path,
    List<JsonPathSegment> segments)
  {
    if (path.Length < 2 || path[0] != '.' || path[1] != '.')
      return false;

    path = path[2..];
    if (path.Length > 0 && path[0] == '.')
      path = path[1..];

    if (path.Length > 0 && path[0] == '[')
      ParseRecursiveBracketSegment(ref path, segments);
    else
      ParseRecursivePropertySegment(ref path, segments);

    return true;
  }

  private static void ParseRecursiveBracketSegment(
    ref ReadOnlySpan<char> path,
    List<JsonPathSegment> segments)
  {
    var close = path.IndexOf(']');
    if (close <= 0)
      return;

    var inner = path[1..close];
    path = path[(close + 1)..];

    if (inner.SequenceEqual("*"))
      segments.Add(new JsonPathSegment("*", -1, -1, JsonPathSegmentType.RecursiveDescent));
    else if (int.TryParse(inner, out var idx))
      segments.Add(new JsonPathSegment(null, idx, RecursiveIndexMarker, JsonPathSegmentType.RecursiveDescent));
    else if (TryParseArrayRange(inner, out var start, out var end))
      segments.Add(new JsonPathSegment(null, start, end, JsonPathSegmentType.RecursiveDescent));
  }

  private static void ParseRecursivePropertySegment(
    ref ReadOnlySpan<char> path,
    List<JsonPathSegment> segments)
  {
    var end = path.IndexOfAny('.', '[');
    var name = end < 0 ? path : path[..end];
    if (name.Length == 0)
      return;

    segments.Add(new JsonPathSegment(name.ToString(), -1, -1, JsonPathSegmentType.RecursiveDescent));
    path = end < 0 ? ReadOnlySpan<char>.Empty : path[end..];
  }

  private static void ParseBracketSegment(
    ref ReadOnlySpan<char> path,
    List<JsonPathSegment> segments)
  {
    var close = path.IndexOf(']');
    if (close < 0)
      return;

    var inner = path[1..close];
    path = path[(close + 1)..];

    if (inner.SequenceEqual("*"))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.Wildcard));
    else if (TryParseFilter(inner, out var filterExpression))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.Filter, null, null, filterExpression));
    else if (TryParseComputedIndex(inner, out var computedExpression))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.ComputedIndex, null, null, null, computedExpression));
    else if (TryParseUnion(inner, out var indexUnion, out var propertyUnion))
    {
      if (indexUnion is not null)
        segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.ArrayUnion, indexUnion));
      else
        segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.PropertyUnion, null, propertyUnion));
    }
    else if (TryParseArrayRange(inner, out var rangeStart, out var rangeEnd))
      segments.Add(new JsonPathSegment(null, rangeStart, rangeEnd, JsonPathSegmentType.ArrayRange));
    else if (int.TryParse(inner, out var idx))
      segments.Add(new JsonPathSegment(null, idx, -1, JsonPathSegmentType.ArrayIndex));
  }

  private static bool TryParseUnion(ReadOnlySpan<char> unionStr, out int[]? indexUnion, out string[]? propertyUnion)
  {
    indexUnion = null;
    propertyUnion = null;

    if (unionStr.IndexOf(',') < 0)
      return false;

    var parts = unionStr.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
      return false;

    var indices = new List<int>(parts.Length);
    foreach (var part in parts)
    {
      if (!int.TryParse(part, out var value))
      {
        indices.Clear();
        break;
      }

      indices.Add(value);
    }

    if (indices.Count == parts.Length)
    {
      indexUnion = indices.ToArray();
      return true;
    }

    propertyUnion = parts
      .Select(static p => p.Trim('"', '\''))
      .Where(static p => p.Length > 0)
      .ToArray();
    return propertyUnion.Length > 0;
  }

  private static bool TryParseFilter(ReadOnlySpan<char> filterStr, out string? expression)
  {
    expression = null;

    if (filterStr.Length < 4 || !filterStr.StartsWith("?(") || filterStr[^1] != ')')
      return false;

    var body = filterStr[2..^1].Trim();
    if (body.Length == 0)
      return false;

    expression = body.ToString();
    return true;
  }

  private static bool TryParseComputedIndex(ReadOnlySpan<char> expressionStr, out string? expression)
  {
    expression = null;

    if (expressionStr.Length < 3 || expressionStr[0] != '(' || expressionStr[^1] != ')')
      return false;

    var body = expressionStr[1..^1].Trim();
    if (body.Length == 0)
      return false;

    expression = body.ToString();
    return true;
  }

  private static void ParsePropertySegment(
    ref ReadOnlySpan<char> path,
    List<JsonPathSegment> segments)
  {
    var end = path.IndexOfAny('.', '[');
    var name = end < 0 ? path : path[..end];

    if (name.Length > 0)
    {
      if (name.SequenceEqual("*"))
        segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.Wildcard));
      else
        segments.Add(new JsonPathSegment(name.ToString(), -1, -1, JsonPathSegmentType.Property));

      path = end < 0 ? ReadOnlySpan<char>.Empty : path[end..];
    }
  }

  private static bool TryParseArrayRange(ReadOnlySpan<char> rangeStr, out int start, out int end)
  {
    start = -1;
    end = -1;

    var colonIdx = rangeStr.IndexOf(':');
    if (colonIdx < 0)
      return false;

    var startPart = rangeStr[..colonIdx];
    if (startPart.Length > 0 && !int.TryParse(startPart, out start))
      return false;
    if (startPart.Length == 0)
      start = 0;

    var endPart = rangeStr[(colonIdx + 1)..];
    if (endPart.Length > 0 && !int.TryParse(endPart, out end))
      return false;
    if (endPart.Length == 0)
      end = int.MaxValue;

    return true;
  }
}
