using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JsonPathPlus;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class InternalInfrastructureTests
{
  private const string FilterCandidateJson = """
  {
    "price": 5,
    "name": "alpha",
    "active": true,
    "nested": { "value": 7 },
    "stringNumber": "12.5",
    "nullable": null
  }
  """;

  public static TheoryData<string, bool> ObjectFilterExpressions => new()
  {
    { "@.price == 5", true },
    { "@.price != 6", true },
    { "@.price < 6", true },
    { "@.price <= 5", true },
    { "@.price > 4", true },
    { "@.price >= 5", true },
    { "@.name == \"alpha\"", true },
    { "@.name != 'beta'", true },
    { "@.active == true", true },
    { "@.active != false", true },
    { "@.nested.value == 7", true },
    { "@.stringNumber > 10", true },
    { "@.nullable == null", false },
    { "@.missing == 1", false },
    { "!@.missing", true },
    { "@.price < 5 || @.active == true", true },
    { "@.price < 5 && @.active == true", false },
    { "@.name > \"aardvark\"", true },
  };

  public static TheoryData<JsonNode?, string, bool, int> ComputedIndexExpressions => new()
  {
    { null, "@.length-1", true, 4 },
    { null, "2+2", true, 4 },
    { null, "@.length*1-1", true, 4 },
    { null, "@.length/2", true, 2 },
    { null, "@.length/0", false, 0 },
    { null, "@.missing", false, 0 },
    { null, "2+", false, 0 },
    { null, "", false, 0 },
  };

  [Fact]
  public void ReadOnlyMemoryStream_Properties_ReflectReadOnlySeekableStream()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));

    Assert.True(stream.CanRead);
    Assert.True(stream.CanSeek);
    Assert.False(stream.CanWrite);
    Assert.Equal(5, stream.Length);
    Assert.Equal(0, stream.Position);
  }

  [Fact]
  public void ReadOnlyMemoryStream_Position_Setter_ValidatesBounds()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));

    stream.Position = 3;
    Assert.Equal(3, stream.Position);

    Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 6);
  }

  [Fact]
  public async Task ReadOnlyMemoryStream_ReadsSynchronouslyAndAsynchronously()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));

    var first = new byte[2];
    Assert.Equal(2, stream.Read(first, 0, first.Length));
    Assert.Equal("he", Encoding.UTF8.GetString(first));
    Assert.Equal(2, stream.Position);

    var second = new byte[2];
    Assert.Equal(2, await stream.ReadAsync(second, 0, second.Length, CancellationToken.None));
    Assert.Equal("ll", Encoding.UTF8.GetString(second));
    Assert.Equal(4, stream.Position);

    var third = new byte[2];
    Assert.Equal(1, await stream.ReadAsync(third.AsMemory(0, third.Length), CancellationToken.None));
    Assert.Equal("o", Encoding.UTF8.GetString(third, 0, 1));
    Assert.Equal(5, stream.Position);

    Assert.Equal(0, stream.Read(new byte[1], 0, 1));
    Assert.Equal(0, await stream.ReadAsync(new byte[1], 0, 1, CancellationToken.None));
    Assert.Equal(0, await stream.ReadAsync(new byte[1].AsMemory(0, 1), CancellationToken.None));
  }

  [Fact]
  public async Task ReadOnlyMemoryStream_ReadAsync_ThrowsWhenCancelled()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[2], 0, 2, cts.Token));
    await Assert.ThrowsAsync<OperationCanceledException>(async () => await stream.ReadExactlyAsync(new byte[2].AsMemory(0, 2), cts.Token));
  }

  [Fact]
  public void ReadOnlyMemoryStream_Seek_SupportsBeginCurrentAndEnd()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));

    Assert.Equal(2, stream.Seek(2, SeekOrigin.Begin));
    Assert.Equal(1, stream.Seek(-1, SeekOrigin.Current));
    Assert.Equal(4, stream.Seek(-1, SeekOrigin.End));

    Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
    Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, (SeekOrigin)99));
  }

  [Fact]
  public void ReadOnlyMemoryStream_WriteMembers_ThrowNotSupported()
  {
    using var stream = new ReadOnlyMemoryStream(Encoding.UTF8.GetBytes("hello"));

    stream.Flush();
    Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    Assert.Throws<NotSupportedException>(() => stream.Write([1, 2, 3], 0, 3));
  }

  [Fact]
  public void JsonPathFilterEvaluator_EmptyExpression_ReturnsFalse()
  {
    Assert.False(JsonPathFilterEvaluator.Evaluate(JsonNode.Parse("1"), "   "));
  }

  [Theory]
  [MemberData(nameof(ObjectFilterExpressions))]
  public void JsonPathFilterEvaluator_ObjectExpressions_ReturnExpected(string expression, bool expected)
  {
    var candidate = JsonNode.Parse(FilterCandidateJson);

    Assert.Equal(expected, JsonPathFilterEvaluator.Evaluate(candidate, expression));
  }

  [Fact]
  public void JsonPathFilterEvaluator_CurrentValueExpressions_ReturnExpected()
  {
    Assert.True(JsonPathFilterEvaluator.Evaluate(JsonValue.Create(5), "@ > 3"));
    Assert.True(JsonPathFilterEvaluator.Evaluate(JsonValue.Create("8"), "@ >= 7"));
    Assert.True(JsonPathFilterEvaluator.Evaluate(JsonValue.Create(true), "@ == true"));
    Assert.True(JsonPathFilterEvaluator.Evaluate(JsonNode.Parse("{\"x\":1}"), "@ == @"));
    Assert.True(JsonPathFilterEvaluator.Evaluate(JsonNode.Parse(FilterCandidateJson), "(@.price < 1) || @.active == true"));
    Assert.False(JsonPathFilterEvaluator.Evaluate(JsonValue.Create(false), "@ == true"));
    Assert.False(JsonPathFilterEvaluator.Evaluate(JsonNode.Parse(FilterCandidateJson), "unknown_token"));
  }

  [Theory]
  [MemberData(nameof(ComputedIndexExpressions))]
  public void JsonPathComputedExpressionEvaluator_Expressions_ReturnExpected(JsonNode? _, string expression, bool expectedSuccess, int expectedIndex)
  {
    var success = JsonPathComputedExpressionEvaluator.TryEvaluateIndex(5, expression, out var index);

    Assert.Equal(expectedSuccess, success);
    if (success)
      Assert.Equal(expectedIndex, index);
  }

  [Fact]
  public void JsonPathFilterEvaluator_PrivateHelpers_CoverNumericAndJsonElementFallbacks()
  {
    var toPrimitive = typeof(JsonPathFilterEvaluator).GetMethod("ToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;
    var tryToDecimal = typeof(JsonPathFilterEvaluator).GetMethod("TryToDecimal", BindingFlags.NonPublic | BindingFlags.Static)!;

    Assert.Equal(5L, toPrimitive.Invoke(null, [JsonValue.Create(5L)]));
    Assert.Equal(5.5d, toPrimitive.Invoke(null, [JsonValue.Create(5.5d)]));

    using var numberDocument = JsonDocument.Parse("12345678901234567890");
    var numberElementNode = JsonValue.Create(numberDocument.RootElement);
    Assert.NotNull(toPrimitive.Invoke(null, [numberElementNode]));

    using var stringDocument = JsonDocument.Parse("\"hello\"");
    Assert.Equal("hello", toPrimitive.Invoke(null, [JsonValue.Create(stringDocument.RootElement)]));

    using var trueDocument = JsonDocument.Parse("true");
    Assert.Equal(true, toPrimitive.Invoke(null, [JsonValue.Create(trueDocument.RootElement)]));

    using var nullDocument = JsonDocument.Parse("null");
    Assert.Null(toPrimitive.Invoke(null, [JsonValue.Create(nullDocument.RootElement)]));

    AssertTryToDecimal(tryToDecimal, (byte)1, true, 1m);
    AssertTryToDecimal(tryToDecimal, (sbyte)2, true, 2m);
    AssertTryToDecimal(tryToDecimal, (short)3, true, 3m);
    AssertTryToDecimal(tryToDecimal, (ushort)4, true, 4m);
    AssertTryToDecimal(tryToDecimal, 5, true, 5m);
    AssertTryToDecimal(tryToDecimal, (uint)6, true, 6m);
    AssertTryToDecimal(tryToDecimal, 7L, true, 7m);
    AssertTryToDecimal(tryToDecimal, (ulong)8, true, 8m);
    AssertTryToDecimal(tryToDecimal, 9.5f, true, 9.5m);
    AssertTryToDecimal(tryToDecimal, 10.5d, true, 10.5m);
    AssertTryToDecimal(tryToDecimal, 11.5m, true, 11.5m);
    AssertTryToDecimal(tryToDecimal, "12.5", true, 12.5m);
    AssertTryToDecimal(tryToDecimal, new object(), false, 0m);
  }

  [Fact]
  public void JsonPathParser_ParsesRecursiveBracketAndProjectionVariants()
  {
    var recursiveWildcard = JsonPathParser.Parse("$..[*]");
    Assert.Single(recursiveWildcard);
    Assert.Equal(JsonPathSegmentType.RecursiveDescent, recursiveWildcard[0].SegmentType);
    Assert.Equal("*", recursiveWildcard[0].PropertyName);

    var recursiveIndex = JsonPathParser.Parse("$..[1]");
    Assert.Single(recursiveIndex);
    Assert.Equal(1, recursiveIndex[0].ArrayIndex);
    Assert.Equal(int.MinValue, recursiveIndex[0].ArrayRangeEnd);

    var recursiveRange = JsonPathParser.Parse("$..[1:3]");
    Assert.Single(recursiveRange);
    Assert.Equal(1, recursiveRange[0].ArrayIndex);
    Assert.Equal(3, recursiveRange[0].ArrayRangeEnd);

    var truncatedRecursive = JsonPathParser.Parse("$..[");
    Assert.Empty(truncatedRecursive);

    var propertyUnion = JsonPathParser.Parse("$['a','b']");
    Assert.Single(propertyUnion);
    Assert.Equal(JsonPathSegmentType.PropertyUnion, propertyUnion[0].SegmentType);

    var mixedUnion = JsonPathParser.Parse("$['a',b]");
    Assert.Empty(mixedUnion);

    var fieldExclusion = JsonPathParser.Parse("$[!title, !price]");
    Assert.Single(fieldExclusion);
    Assert.Equal(JsonPathSegmentType.FieldExclusion, fieldExclusion[0].SegmentType);

    var fieldProjection = JsonPathParser.Parse("$[title, author]");
    Assert.Single(fieldProjection);
    Assert.Equal(JsonPathSegmentType.FieldProjection, fieldProjection[0].SegmentType);

    var invalidNestedQuery = JsonPathParser.Parse("$[name, other]");
    Assert.Single(invalidNestedQuery);
    Assert.Equal(JsonPathSegmentType.FieldProjection, invalidNestedQuery[0].SegmentType);
  }

  [Fact]
  public void JsonPathExtractionCore_EmptySegmentCases_ReturnRootAndRootPath()
  {
    var root = JsonNode.Parse("{\"value\": 1}");
    var empty = JsonPathExtractionCore.ParseSegments(null);

    Assert.Empty(empty);
    Assert.Same(root, JsonPathExtractionCore.FindFirstMatch(root, empty, 0));
    Assert.Same(root, JsonPathExtractionCore.FindFirstMatch(root, Segments(Property("value")), 1));

    var allMatches = JsonPathExtractionCore.FindAllMatches(root, Segments(Property("value")), 1).ToList();
    Assert.Single(allMatches);
    Assert.Same(root, allMatches[0]);

    var pathMatches = JsonPathExtractionCore.FindAllMatchesWithPaths(root, Segments(Property("value")), 1, "$[0]").ToList();
    Assert.Single(pathMatches);
    Assert.Equal("$[0]", pathMatches[0].Path);
    Assert.Same(root, pathMatches[0].Value);
  }

  private static List<JsonPathSegment> Segments(params JsonPathSegment[] segments)
    => new(segments);

  private static JsonPathSegment Property(string name)
    => new(name, -1, -1, JsonPathSegmentType.Property);

  private static void AssertTryToDecimal(MethodInfo method, object value, bool expectedSuccess, decimal expectedNumber)
  {
    object?[] args = [value, 0m];
    var success = (bool)method.Invoke(null, args)!;

    Assert.Equal(expectedSuccess, success);
    if (expectedSuccess)
      Assert.Equal(expectedNumber, (decimal)args[1]!);
  }
}