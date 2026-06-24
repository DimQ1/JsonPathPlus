using System;
using System.Collections.Generic;

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
    var close = FindMatchingCloseBracket(path);
    if (close <= 0)
    {
      path = ReadOnlySpan<char>.Empty;
      return;
    }

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
    var close = FindMatchingCloseBracket(path);
    if (close < 0)
    {
      path = ReadOnlySpan<char>.Empty;
      return;
    }

    var inner = path[1..close];
    path = path[(close + 1)..];

    if (inner.Length == 1 && inner[0] == '*')
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.Wildcard));
    else if (TryParseFilter(inner, out var filterExpression))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.Filter, null, null, filterExpression));
    else if (TryParseComputedIndex(inner, out var computedExpression))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.ComputedIndex, null, null, null, computedExpression));
    else if (IsArrayRangeCandidate(inner) && TryParseArrayRange(inner, out var rangeStart, out var rangeEnd))
      segments.Add(new JsonPathSegment(null, rangeStart, rangeEnd, JsonPathSegmentType.ArrayRange));
    else if (IsIntegerCandidate(inner) && int.TryParse(inner, out var idx))
      segments.Add(new JsonPathSegment(null, idx, -1, JsonPathSegmentType.ArrayIndex));
    else if (inner.IndexOf('[') >= 0 && TryParseNestedQuery(inner, out var nestedBranches))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.NestedQuery, null, null, null, null, null, nestedBranches));
    else if (TryParseUnion(inner, out var indexUnion, out var propertyUnion))
    {
      if (indexUnion is not null)
        segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.ArrayUnion, indexUnion));
      else
        segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.PropertyUnion, null, propertyUnion));
    }
    else if (TryParseExistFunction(inner, out var existField))
      segments.Add(new JsonPathSegment(existField, -1, -1, JsonPathSegmentType.FieldExistence));
    else if (TryParseCountFunction(inner, out var countField))
      segments.Add(new JsonPathSegment(countField, -1, -1, JsonPathSegmentType.FieldCount));
    else if (TryParseFieldExclusion(inner, out var exclusionFields))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.FieldExclusion, null, null, null, null, exclusionFields));
    else if (TryParseFieldProjection(inner, out var projectionFields))
      segments.Add(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.FieldProjection, null, null, null, null, projectionFields));
  }

  private static bool IsArrayRangeCandidate(ReadOnlySpan<char> value)
  {
    var trimmed = value.Trim();
    if (trimmed.Length == 0 || trimmed.IndexOf(':') < 0)
      return false;

    var first = trimmed[0];
    return first == ':' || first == '-' || IsAsciiDigit(first);
  }

  private static bool IsIntegerCandidate(ReadOnlySpan<char> value)
  {
    var trimmed = value.Trim();
    if (trimmed.Length == 0)
      return false;

    var first = trimmed[0];
    return first == '-' || IsAsciiDigit(first);
  }

  private static bool IsAsciiDigit(char value)
    => value >= '0' && value <= '9';

  private static bool TryParseUnion(ReadOnlySpan<char> unionStr, out int[]? indexUnion, out string[]? propertyUnion)
  {
    indexUnion = null;
    propertyUnion = null;

    if (unionStr.IndexOf(',') < 0)
      return false;

    // Count parts to pre-size collections.
    var partCount = 1;
    for (var i = 0; i < unionStr.Length; i++)
    {
      if (unionStr[i] == ',')
        partCount++;
    }

    // First pass: try integer indices.
    var indices = new List<int>(partCount);
    foreach (var slice in SplitAndTrim(unionStr, ','))
    {
      if (!int.TryParse(slice, out var value))
      {
        indices.Clear();
        break;
      }

      indices.Add(value);
    }

    if (indices.Count > 0)
    {
      indexUnion = indices.ToArray();
      return true;
    }

    // Second pass: property union (quoted strings).
    propertyUnion = new string[partCount];
    var idx = 0;
    foreach (var slice in SplitAndTrim(unionStr, ','))
    {
      if (slice.IsEmpty)
        return false;

      // Must start and end with quotes
      if ((slice[0] == '"' && slice[^1] == '"') || (slice[0] == '\'' && slice[^1] == '\''))
      {
        propertyUnion[idx++] = slice[1..^1].ToString();
      }
      else
      {
        return false;
      }
    }

    return idx > 0;
  }

  /// <summary>
  /// Yields trimmed spans around the separator without allocating strings.
  /// </summary>
  private static ReadOnlySpanSplitter SplitAndTrim(ReadOnlySpan<char> source, char separator)
    => new(source, separator);

  private ref struct ReadOnlySpanSplitter
  {
    private ReadOnlySpan<char> _remaining;
    private readonly char _separator;

    public ReadOnlySpanSplitter(ReadOnlySpan<char> source, char separator)
    {
      _remaining = source;
      _separator = separator;
    }

    public ReadOnlySpanSplitter GetEnumerator() => this;

    public ReadOnlySpan<char> Current { get; private set; }

    public bool MoveNext()
    {
      while (_remaining.Length > 0)
      {
        var commaIndex = _remaining.IndexOf(_separator);
        if (commaIndex < 0)
        {
          Current = _remaining.Trim();
          _remaining = default;
          return Current.Length > 0;
        }

        Current = _remaining[..commaIndex].Trim();
        _remaining = _remaining[(commaIndex + 1)..];
        if (Current.Length > 0)
          return true;
        // Skip empty entries (consecutive separators).
      }

      return false;
    }
  }

  private static bool TryParseNestedQuery(ReadOnlySpan<char> inner, out NestedQueryBranch[]? branches)
  {
    branches = null;

    // Must contain a comma or a bracket indicating sub-paths
    if (inner.IndexOf(',') < 0 && inner.IndexOf('[') < 0)
      return false;

    var parts = SplitTopLevelCommas(inner);
    if (parts.Count == 0)
      return false;

    var branchList = new List<NestedQueryBranch>();
    var hasSubPath = false;

    foreach (var part in parts)
    {
      var trimmed = part.Trim();
      if (trimmed.Length == 0)
        return false;

      // Must not be quoted or numeric — those belong to other segment types
      if (trimmed[0] == '"' || trimmed[0] == '\'')
        return false;

      // Find the first '[' to separate key name from sub-paths
      var bracketPos = trimmed.IndexOf('[');
      string keyName;
      List<JsonPathSegment>? subSegments = null;

      if (bracketPos < 0)
      {
        keyName = trimmed.ToString();
      }
      else
      {
        keyName = trimmed[..bracketPos].ToString();
        var subPath = trimmed[bracketPos..];
        subSegments = Parse(subPath);
        hasSubPath = true;
      }

      // Validate key name: must be a valid bare identifier, not numeric
      if (keyName.Length == 0)
        return false;
      if (int.TryParse(keyName, out _))
        return false;
      if (!IsValidIdentifier(keyName))
        return false;

      branchList.Add(new NestedQueryBranch(keyName, subSegments ?? new List<JsonPathSegment>()));
    }

    // Must have at least one branch with sub-path to distinguish from field projection
    if (!hasSubPath)
      return false;

    branches = branchList.ToArray();
    return true;
  }

  private static List<string> SplitTopLevelCommas(ReadOnlySpan<char> input)
  {
    var parts = new List<string>();
    var depth = 0;
    var start = 0;

    for (var i = 0; i < input.Length; i++)
    {
      switch (input[i])
      {
        case '[':
          depth++;
          break;
        case ']':
          if (depth > 0)
            depth--;
          break;
        case ',':
          if (depth == 0)
          {
            parts.Add(input[start..i].ToString());
            start = i + 1;
          }
          break;
      }
    }

    if (start < input.Length)
      parts.Add(input[start..].ToString());

    return parts;
  }

  private static int FindMatchingCloseBracket(ReadOnlySpan<char> span)
  {
    var depth = 0;
    for (var i = 0; i < span.Length; i++)
    {
      switch (span[i])
      {
        case '[':
          depth++;
          break;
        case ']':
          depth--;
          if (depth == 0)
            return i;
          break;
      }
    }

    return -1;
  }

  private static bool TryParseFieldExclusion(ReadOnlySpan<char> exclusionStr, out string[]? fields)
  {
    fields = null;

    // Check if it starts with '!' (required for field exclusion)
    if (exclusionStr.Length == 0 || exclusionStr[0] != '!')
      return false;

    var fieldList = new List<string>();
    foreach (var part in SplitAndTrim(exclusionStr, ','))
    {
      // Must start with '!' for exclusion
      if (part.Length == 0 || part[0] != '!')
        return false;

      var fieldName = part[1..]; // Remove the '!' prefix

      // Skip if it's a number (that's for array union)
      if (int.TryParse(fieldName, out _))
        return false;

      // Check if it's a valid identifier
      if (!IsValidIdentifierSpan(fieldName))
        return false;

      fieldList.Add(fieldName.ToString());
    }

    if (fieldList.Count > 0)
    {
      fields = fieldList.ToArray();
      return true;
    }

    return false;
  }

  private static bool TryParseFieldProjection(ReadOnlySpan<char> projectionStr, out string[]? fields)
  {
    fields = null;

    if (projectionStr.Length == 0)
      return false;

    var fieldList = new List<string>();
    foreach (var part in SplitAndTrim(projectionStr, ','))
    {
      // Skip if it's quoted (that's for property union)
      if (part[0] == '"' || part[0] == '\'')
        return false;

      // Skip if it's a number (that's for array union)
      if (int.TryParse(part, out _))
        return false;

      // Check if it's a valid identifier
      if (!IsValidIdentifierSpan(part))
        return false;

      fieldList.Add(part.ToString());
    }

    if (fieldList.Count > 0)
    {
      fields = fieldList.ToArray();
      return true;
    }

    return false;
  }

  private static bool TryParseExistFunction(ReadOnlySpan<char> functionStr, out string? fieldName)
  {
    fieldName = null;

    const string prefix = "exist(";
    if (!functionStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || functionStr[^1] != ')')
      return false;

    var inner = functionStr[prefix.Length..^1].Trim();
    if (inner.Length == 0)
      return false;

    var field = inner.ToString();
    if (field != "*" && !IsValidIdentifier(field))
      return false;

    fieldName = field;
    return true;
  }

  private static bool TryParseCountFunction(ReadOnlySpan<char> functionStr, out string? fieldName)
  {
    fieldName = null;

    const string prefix = "count(";
    if (!functionStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || functionStr[^1] != ')')
      return false;

    var inner = functionStr[prefix.Length..^1].Trim();
    if (inner.Length == 0)
      return false;

    var field = inner.ToString();
    if (field != "*" && !IsValidIdentifier(field))
      return false;

    fieldName = field;
    return true;
  }

  private static bool IsValidIdentifierSpan(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
      return false;

    // First character must be letter or underscore
    if (!char.IsLetter(name[0]) && name[0] != '_')
      return false;

    // Remaining characters must be letters, digits, or underscores
    for (int i = 1; i < name.Length; i++)
    {
      if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
        return false;
    }

    return true;
  }

  private static bool IsValidIdentifier(string name)
  {
    if (string.IsNullOrEmpty(name))
      return false;

    // First character must be letter or underscore
    if (!char.IsLetter(name[0]) && name[0] != '_')
      return false;

    // Remaining characters must be letters, digits, or underscores
    for (int i = 1; i < name.Length; i++)
    {
      if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
        return false;
    }

    return true;
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
