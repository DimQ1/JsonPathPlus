using System;
using System.Collections.Generic;

namespace JsonPathPlus;

internal readonly record struct JsonPathSegment(
  string? PropertyName,
  int ArrayIndex,
  int ArrayRangeEnd,
  JsonPathSegmentType SegmentType,
  int[]? ArrayUnionIndices = null,
  string[]? PropertyUnionNames = null,
  string? FilterExpression = null,
  string? ComputedIndexExpression = null,
  string[]? ProjectionFields = null,
  NestedQueryBranch[]? NestedQueryBranches = null);

internal readonly record struct NestedQueryBranch(
  string PropertyName,
  List<JsonPathSegment> SubSegments);

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
  RecursiveDescent,
  FieldProjection,
  FieldExclusion,
  FieldExistence,
  FieldCount,
  NestedQuery
}
