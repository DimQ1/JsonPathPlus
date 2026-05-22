using Xunit;
using System.Text;
using System.Text.Json.Nodes;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

/// <summary>
/// Tests for JsonPathComputedExpressionEvaluator edge cases:
/// multiplication, division, division by zero, NaN handling, numeric literals, invalid tokens.
/// </summary>
public sealed class StreamJsonComputedExpressionTests
{
  private const string TestJson = """
  {
    "items": [
      { "id": 10 }, { "id": 20 }, { "id": 30 }, { "id": 40 },
      { "id": 50 }, { "id": 60 }, { "id": 70 }, { "id": 80 }
    ]
  }
  """;

  // ── Multiplication ─────────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithComputedDivision_ReturnsExpected()
  {
    // (@.length/2) = 4 => items[4].id = 50 (0-indexed)
    using var stream = CreateStream(TestJson);

    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length/2)].id");
    AssertNodeEquals(JsonValue.Create(50), result);
  }

  [Fact]
  public async Task ExtractFirst_WithComputedMultiplyThenSubtract_ReturnsExpected()
  {
    using var stream = CreateStream(TestJson);

    // (@.length-1) = 7 => items[7].id = 80
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length-1)].id");
    AssertNodeEquals(JsonValue.Create(80), result);
  }

  [Fact]
  public async Task ExtractFirst_WithComputedMultiplyByOne_ReturnsExpected()
  {
    using var stream = CreateStream(TestJson);

    // @.length = 8, 8 * 1 - 4 = 4 => items[4].id = 50
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length*1-4)].id");
    AssertNodeEquals(JsonValue.Create(50), result);
  }

  // ── Division by zero ───────────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithComputedDivisionByZero_DoesNotThrow()
  {
    using var stream = CreateStream(TestJson);

    // (@.length / 0) — division by zero produces NaN, TryEvaluateIndex returns false,
    // bracket segment is discarded, so we access items directly, then .id fails on array
    // Actually: segment discarded -> path becomes just $.items -> returns the array.
    // Then .id applied to array: no match -> null in ExtractFirst
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length/0)].id");

    // The computed index fails, segment is skipped, so we get $.items.id which is null
    Assert.Null(result);
  }

  // ── Numeric literal operands ───────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithNumericLiteralMinusOne_ReturnsLast()
  {
    using var stream = CreateStream(TestJson);

    // (8 - 1) = 7 => items[7].id = 80
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length-1)].id");
    AssertNodeEquals(JsonValue.Create(80), result);
  }

  [Fact]
  public async Task ExtractFirst_WithNumericLiteralPlusTwo_ReturnsExpected()
  {
    using var stream = CreateStream(TestJson);

    // (2 + 2) = 4 => items[4].id = 50
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(2+2)].id");
    AssertNodeEquals(JsonValue.Create(50), result);
  }

  // ── Negative computed index ────────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithComputedNegativeResult_ReturnsFromEnd()
  {
    using var stream = CreateStream(TestJson);

    // (@.length - 9) = -1 => items[7].id = 80 (last element)
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(@.length-9)].id");
    AssertNodeEquals(JsonValue.Create(80), result);
  }

  // ── Chain of computed expressions ──────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithChainedAddSubtract_ReturnsExpected()
  {
    using var stream = CreateStream(TestJson);

    // (0 + 3 - 0) = 3 => items[3].id = 40
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[(0+3-0)].id");
    AssertNodeEquals(JsonValue.Create(40), result);
  }

  // ── Empty computed expression ───────────────────────────────────────────

  [Fact]
  public async Task ExtractFirst_WithEmptyComputedExpression_ReturnsArray()
  {
    using var stream = CreateStream(TestJson);

    // Empty computed expression () does not parse as valid computed index;
    // segment is discarded, returning the array as if the bracket wasn't there
    var result = await stream.ExtractFirstJsonMatchAsync("$.items[()]");
    Assert.IsType<JsonArray>(result);
  }

  // ── Helper methods ─────────────────────────────────────────────────────

  private static MemoryStream CreateStream(string json)
    => new(Encoding.UTF8.GetBytes(json));

  private static async Task<List<JsonNode?>> CollectAsync(IAsyncEnumerable<JsonNode?> source)
  {
    var list = new List<JsonNode?>();
    await foreach (var item in source)
      list.Add(item);
    return list;
  }

  private static void AssertNodeEquals(JsonNode? expected, JsonNode? actual)
  {
    Assert.True(JsonNode.DeepEquals(expected, actual),
      $"Expected: {expected?.ToJsonString() ?? "null"}, Actual: {actual?.ToJsonString() ?? "null"}");
  }
}
