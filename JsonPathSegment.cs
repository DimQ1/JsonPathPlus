using System;

namespace JsonPathPlus;

internal readonly record struct JsonPathSegment(
  string? PropertyName,
  int ArrayIndex,
  int ArrayRangeEnd,
  JsonPathSegmentType SegmentType,
  int[]? ArrayUnionIndices = null,
  string[]? PropertyUnionNames = null,
  string? FilterExpression = null,
  string? ComputedIndexExpression = null);

internal enum JsonPathSegmentType
{
  Property,
  ArrayIndex,
  ArrayRange,
  ArrayUnion,
  PropertyUnion,
  Filter,
  ComputedIndex,
  Wildcard,
  RecursiveDescent
}
