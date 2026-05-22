using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using JsonPathPlus;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class JsonPathMatcherInternalTests
{
  private const string SampleJson = """
  {
    "simple": "value",
    "complex name": {
      "quote'key": 1,
      "slash\\key": 2
    },
    "array": [
      { "id": 1, "name": "a", "enabled": true, "meta": { "rank": 10 } },
      { "id": 2, "name": "b", "meta": { "rank": 20 } },
      { "id": 3, "name": "c", "enabled": false, "meta": { "rank": 30 } }
    ],
    "obj": { "p1": "x", "p2": "y" },
    "nested": {
      "prop": "top",
      "children": [
        { "prop": "first" },
        { "other": 1 }
      ]
    }
  }
  """;

  [Fact]
  public void FindFirstMatch_AndFindMatches_CoverBasicSegments()
  {
    var root = ParseRoot();

    AssertNodeEquals(JsonValue.Create("value"), JsonPathMatcher.FindFirstMatch(root, Segments(Property("simple"))));
    AssertNodeEquals(JsonValue.Create(3), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), ArrayIndex(-1), Property("id"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(root, Segments(Property("missing"))));

    var rangeMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), ArrayRange(1, 3), Property("id")));
    Assert.Equal(2, rangeMatches.Count);
    AssertNodeEquals(JsonValue.Create(2), rangeMatches[0]);
    AssertNodeEquals(JsonValue.Create(3), rangeMatches[1]);

    var unionMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), ArrayUnion(0, 2), Property("name")));
    Assert.Equal(2, unionMatches.Count);
    AssertNodeEquals(JsonValue.Create("a"), unionMatches[0]);
    AssertNodeEquals(JsonValue.Create("c"), unionMatches[1]);
  }

  [Fact]
  public void FindMatches_CoverFilterComputedWildcardAndObjectFilter()
  {
    var root = ParseRoot();

    var filterMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), Filter("@.id >= 2"), Property("name")));
    Assert.Equal(2, filterMatches.Count);
    AssertNodeEquals(JsonValue.Create("b"), filterMatches[0]);
    AssertNodeEquals(JsonValue.Create("c"), filterMatches[1]);

    var objectFilterMatches = JsonPathMatcher.FindMatches(root, Segments(Property("obj"), Filter("@ == 'x'")));
    Assert.Single(objectFilterMatches);
    AssertNodeEquals(JsonValue.Create("x"), objectFilterMatches[0]);

    AssertNodeEquals(JsonValue.Create(3), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), Computed("@.length-1"), Property("id"))));

    var wildcardMatches = JsonPathMatcher.FindMatches(root, Segments(Property("obj"), Wildcard()));
    Assert.Equal(2, wildcardMatches.Count);
    AssertNodeEquals(JsonValue.Create("x"), wildcardMatches[0]);
    AssertNodeEquals(JsonValue.Create("y"), wildcardMatches[1]);
  }

  [Fact]
  public void FindMatches_CoverRecursiveSegments()
  {
    var root = ParseRoot();

    var propertyMatches = JsonPathMatcher.FindMatches(root, Segments(RecursiveProperty("prop")));
    Assert.Equal(2, propertyMatches.Count);
    AssertNodeEquals(JsonValue.Create("top"), propertyMatches[0]);
    AssertNodeEquals(JsonValue.Create("first"), propertyMatches[1]);

    var wildcardMatches = JsonPathMatcher.FindMatches(root, Segments(RecursiveWildcard()));
    Assert.True(wildcardMatches.Count >= 6);

    var arrayIndexMatches = JsonPathMatcher.FindMatches(root, Segments(RecursiveIndex(0)));
    Assert.Equal(2, arrayIndexMatches.Count);

    var arrayRangeMatches = JsonPathMatcher.FindMatches(root, Segments(RecursiveRange(0, 2)));
    Assert.Equal(4, arrayRangeMatches.Count);
  }

  [Fact]
  public void FindMatches_CoverFieldSegments()
  {
    var root = ParseRoot();

    var projectionMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), FieldProjection("id", "name")));
    Assert.Equal(3, projectionMatches.Count);
    var projection = Assert.IsType<JsonObject>(projectionMatches[0]);
    Assert.True(projection.ContainsKey("id"));
    Assert.True(projection.ContainsKey("name"));
    Assert.False(projection.ContainsKey("meta"));

    var exclusionMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), FieldExclusion("meta")));
    Assert.Equal(3, exclusionMatches.Count);
    var excluded = Assert.IsType<JsonObject>(exclusionMatches[0]);
    Assert.False(excluded.ContainsKey("meta"));
    Assert.True(excluded.ContainsKey("id"));

    var existenceMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), FieldExistence("enabled")));
    Assert.Single(existenceMatches);
    AssertNodeEquals(JsonValue.Create(true), existenceMatches[0]);

    var countMatches = JsonPathMatcher.FindMatches(root, Segments(Property("array"), FieldCount("enabled")));
    Assert.Single(countMatches);
    AssertNodeEquals(JsonValue.Create(2), countMatches[0]);

    var wildcardExistence = JsonPathMatcher.FindMatches(root, Segments(Property("obj"), FieldExistence("*")));
    Assert.Single(wildcardExistence);
    AssertNodeEquals(JsonValue.Create(true), wildcardExistence[0]);

    var wildcardCount = JsonPathMatcher.FindMatches(root, Segments(Property("obj"), FieldCount("*")));
    Assert.Single(wildcardCount);
    AssertNodeEquals(JsonValue.Create(2), wildcardCount[0]);

    Assert.Empty(JsonPathMatcher.FindMatches(root, Segments(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.FieldProjection, null, null, null, null, []))));
    Assert.Empty(JsonPathMatcher.FindMatches(root, Segments(new JsonPathSegment(null, -1, -1, JsonPathSegmentType.FieldExistence))));
  }

  [Fact]
  public void FindMatches_CoverNestedQuery()
  {
    var root = ParseRoot();
    var branches = new[]
    {
      new NestedQueryBranch("array", new List<JsonPathSegment> { Filter("@.id >= 2"), FieldProjection("name") }),
      new NestedQueryBranch("simple", new List<JsonPathSegment>()),
      new NestedQueryBranch("missing", new List<JsonPathSegment> { Property("value") })
    };

    var result = Assert.IsType<JsonObject>(JsonPathMatcher.FindFirstMatch(root, Segments(NestedQuery(branches))));
    Assert.Equal("value", result["simple"]?.GetValue<string>());
    Assert.False(result.ContainsKey("missing"));

    var array = Assert.IsType<JsonArray>(result["array"]);
    Assert.Equal(2, array.Count);
    Assert.Equal("b", array[0]?["name"]?.GetValue<string>());
    Assert.Equal("c", array[1]?["name"]?.GetValue<string>());
  }

  [Fact]
  public void FindFirstMatch_CoversVisitorBranchesAcrossSegmentTypes()
  {
    var root = ParseRoot();

    AssertNodeEquals(JsonValue.Create(2), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), ArrayRange(1, 3), Property("id"))));
    AssertNodeEquals(JsonValue.Create("b"), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), ArrayUnion(1, 2), Property("name"))));
    AssertNodeEquals(JsonValue.Create("x"), JsonPathMatcher.FindFirstMatch(root, Segments(Property("obj"), PropertyUnion("missing", "p1"))));
    AssertNodeEquals(JsonValue.Create("b"), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), Filter("@.id >= 2"), Property("name"))));
    AssertNodeEquals(JsonValue.Create(3), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), Computed("@.length-1"), Property("id"))));
    AssertNodeEquals(JsonValue.Create("a"), JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), Wildcard(), Property("name"))));
    AssertNodeEquals(JsonValue.Create("top"), JsonPathMatcher.FindFirstMatch(root, Segments(RecursiveProperty("prop"))));

    var projected = Assert.IsType<JsonObject>(JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), FieldProjection("name"))));
    Assert.Equal("a", projected["name"]?.GetValue<string>());

    var excluded = Assert.IsType<JsonObject>(JsonPathMatcher.FindFirstMatch(root, Segments(Property("array"), FieldExclusion("meta"))));
    Assert.False(excluded.ContainsKey("meta"));

    AssertNodeEquals(JsonValue.Create(true), JsonPathMatcher.FindFirstMatch(root, Segments(Property("obj"), FieldExistence("*"))));
    AssertNodeEquals(JsonValue.Create(2), JsonPathMatcher.FindFirstMatch(root, Segments(Property("obj"), FieldCount("*"))));

    var nested = Assert.IsType<JsonObject>(JsonPathMatcher.FindFirstMatch(root, Segments(NestedQuery([
      new NestedQueryBranch("array", new List<JsonPathSegment> { Filter("@.id == 1"), FieldProjection("name") }),
      new NestedQueryBranch("simple", new List<JsonPathSegment>())
    ]))));
    Assert.Equal("value", nested["simple"]?.GetValue<string>());
  }

  [Fact]
  public void FindFirstMatch_OnScalarInput_CoversEarlyReturnBranches()
  {
    var scalar = JsonValue.Create(42);

    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(ArrayIndex(0))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(ArrayRange(0, 2))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(ArrayUnion(0, 1))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(PropertyUnion("p1"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(Filter("@ > 0"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(Computed("0"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(Wildcard())));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(FieldProjection("id"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(FieldExclusion("id"))));
    Assert.Null(JsonPathMatcher.FindFirstMatch(scalar, Segments(NestedQuery([
      new NestedQueryBranch("value", new List<JsonPathSegment>())
    ]))));
  }

  [Fact]
  public void FindFirstMatch_RecursiveArrayIndex_ReturnsNestedArrayItem()
  {
    var root = ParseRoot();

    var match = JsonPathMatcher.FindFirstMatch(root, Segments(RecursiveArrayIndex(1)));

    var obj = Assert.IsType<JsonObject>(match);
    Assert.Equal(2, obj["id"]?.GetValue<int>());
  }

  [Fact]
  public void FindMatchesWithPaths_RecursiveArrayRange_ReturnsIndexedPaths()
  {
    var root = ParseRoot();

    var matches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(RecursiveArrayRange(1, 3))).ToList();

    Assert.Contains(matches, m => m.Path == "$.array[1]");
    Assert.Contains(matches, m => m.Path == "$.array[2]");
  }

  [Fact]
  public void FindMatchesWithPaths_EscapesComplexPropertyNames()
  {
    var root = ParseRoot();

    var matches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("complex name"), Wildcard()));
    Assert.Equal(2, matches.Count);
    Assert.Equal("$['complex name']['quote\\'key']", matches[0].Path);
    Assert.Equal("$['complex name']['slash\\\\key']", matches[1].Path);
  }

  [Fact]
  public void FindMatchesWithPaths_CoverFieldSegmentsAndPropertyUnion()
  {
    var root = ParseRoot();

    var projectionMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("array"), FieldProjection("id")));
    Assert.Equal(3, projectionMatches.Count);
    Assert.Equal("$.array[0]", projectionMatches[0].Path);
    Assert.Equal("$.array[1]", projectionMatches[1].Path);
    Assert.Equal("$.array[2]", projectionMatches[2].Path);

    var exclusionMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("array"), FieldExclusion("meta")));
    Assert.Equal(3, exclusionMatches.Count);
    Assert.Equal("$.array[0]", exclusionMatches[0].Path);

    var existenceMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("obj"), FieldExistence("*")));
    Assert.Single(existenceMatches);
    Assert.Equal("$.obj", existenceMatches[0].Path);
    AssertNodeEquals(JsonValue.Create(true), existenceMatches[0].Value);

    var countMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("obj"), FieldCount("*")));
    Assert.Single(countMatches);
    Assert.Equal("$.obj", countMatches[0].Path);
    AssertNodeEquals(JsonValue.Create(2), countMatches[0].Value);

    var unionMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(PropertyUnion("simple", "complex name")));
    Assert.Equal(2, unionMatches.Count);
    Assert.Equal("$.simple", unionMatches[0].Path);
    Assert.Equal("$['complex name']", unionMatches[1].Path);
  }

  [Fact]
  public void FindMatchesWithPaths_CoverRecursiveAndObjectFilterPaths()
  {
    var root = ParseRoot();

    var recursiveMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(RecursiveProperty("prop")));
    Assert.Equal(2, recursiveMatches.Count);
    Assert.Equal("$.nested.prop", recursiveMatches[0].Path);
    Assert.Equal("$.nested.children[0].prop", recursiveMatches[1].Path);

    var filterMatches = JsonPathMatcher.FindMatchesWithPaths(root, Segments(Property("obj"), Filter("@ == 'y'")));
    Assert.Single(filterMatches);
    Assert.Equal("$.obj.p2", filterMatches[0].Path);
    AssertNodeEquals(JsonValue.Create("y"), filterMatches[0].Value);
  }

  private static JsonNode ParseRoot()
    => JsonNode.Parse(SampleJson)!;

  private static List<JsonPathSegment> Segments(params JsonPathSegment[] segments)
    => new(segments);

  private static JsonPathSegment Property(string name)
    => new(name, -1, -1, JsonPathSegmentType.Property);

  private static JsonPathSegment ArrayIndex(int index)
    => new(null, index, -1, JsonPathSegmentType.ArrayIndex);

  private static JsonPathSegment ArrayRange(int start, int end)
    => new(null, start, end, JsonPathSegmentType.ArrayRange);

  private static JsonPathSegment ArrayUnion(params int[] indices)
    => new(null, -1, -1, JsonPathSegmentType.ArrayUnion, indices);

  private static JsonPathSegment PropertyUnion(params string[] properties)
    => new(null, -1, -1, JsonPathSegmentType.PropertyUnion, null, properties);

  private static JsonPathSegment Filter(string expression)
    => new(null, -1, -1, JsonPathSegmentType.Filter, null, null, expression);

  private static JsonPathSegment Computed(string expression)
    => new(null, -1, -1, JsonPathSegmentType.ComputedIndex, null, null, null, expression);

  private static JsonPathSegment Wildcard()
    => new(null, -1, -1, JsonPathSegmentType.Wildcard);

  private static JsonPathSegment RecursiveWildcard()
    => new("*", -1, -1, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment RecursiveProperty(string name)
    => new(name, -1, -1, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment RecursiveIndex(int index)
    => new(null, index, int.MinValue, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment RecursiveRange(int start, int end)
    => new(null, start, end, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment RecursiveArrayIndex(int index)
    => new(null, index, int.MinValue, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment RecursiveArrayRange(int start, int end)
    => new(null, start, end, JsonPathSegmentType.RecursiveDescent);

  private static JsonPathSegment FieldProjection(params string[] fields)
    => new(null, -1, -1, JsonPathSegmentType.FieldProjection, null, null, null, null, fields);

  private static JsonPathSegment FieldExclusion(params string[] fields)
    => new(null, -1, -1, JsonPathSegmentType.FieldExclusion, null, null, null, null, fields);

  private static JsonPathSegment FieldExistence(string field)
    => new(field, -1, -1, JsonPathSegmentType.FieldExistence);

  private static JsonPathSegment FieldCount(string field)
    => new(field, -1, -1, JsonPathSegmentType.FieldCount);

  private static JsonPathSegment NestedQuery(NestedQueryBranch[] branches)
    => new(null, -1, -1, JsonPathSegmentType.NestedQuery, null, null, null, null, null, branches);

  private static void AssertNodeEquals(JsonNode? expected, JsonNode? actual)
  {
    Assert.True(JsonNode.DeepEquals(expected, actual),
      $"Expected: {expected?.ToJsonString() ?? "null"}, Actual: {actual?.ToJsonString() ?? "null"}");
  }
}