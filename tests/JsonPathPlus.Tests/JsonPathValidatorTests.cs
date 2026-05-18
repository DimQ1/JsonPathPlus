using Xunit;
using JsonPathPlus;

namespace JsonPathPlus.Tests;

public sealed class JsonPathValidatorTests
{
  // ── Valid paths ──────────────────────────────────────────────────────────

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("$")]
  [InlineData("$.name")]
  [InlineData("$.a.b.c")]
  [InlineData("$.items[0]")]
  [InlineData("$.items[*]")]
  [InlineData("$.items[1:3]")]
  [InlineData("$.items[:3]")]
  [InlineData("$.items[2:]")]
  [InlineData("$.items[-1]")]
  [InlineData("$.items[-2:]")]
  [InlineData("$.items[0,2,4]")]
  [InlineData("$.obj[name,age]")]
  [InlineData("$..name")]
  [InlineData("$..*")]
  [InlineData("$.items[?(@.isbn)]")]
  [InlineData("$.items[?(@.price < 10)]")]
  [InlineData("$.items[?(@.p > 1 && @.p < 5)]")]
  [InlineData("$.items[?(!@.isbn)]")]
  [InlineData("$.items[(@.length-1)]")]
  [InlineData("$.items[(@.length/2)]")]
  [InlineData("$.name.")]           // trailing dot — lenient
  [InlineData("$..")]               // double dot with no name — lenient
  [InlineData("$$")]                // $ as property name — lenient, just won't match
  [InlineData("$.items[?(@.name == \"[bracket]\")]")] // ] inside quoted string
  public void Validate_ValidPath_ReturnsIsValidTrue(string? path)
  {
    var result = JsonPathValidator.Validate(path);

    Assert.True(result.IsValid, $"Expected valid for: {path ?? "null"}, but got error: {result.Error}");
    Assert.Null(result.Error);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("$")]
  [InlineData("$.name")]
  [InlineData("$.items[?(@.price < 10)]")]
  public void IsValid_ValidPath_ReturnsTrue(string? path)
  {
    Assert.True(JsonPathValidator.IsValid(path));
  }

  // ── Unclosed bracket ─────────────────────────────────────────────────────

  [Theory]
  [InlineData("$.obj[p2,p1")]
  [InlineData("$[name")]
  [InlineData("$.items[0")]
  [InlineData("$.items[*")]
  [InlineData("$..[name")]
  public void Validate_UnclosedBracket_ReturnsInvalidWithError(string path)
  {
    var result = JsonPathValidator.Validate(path);

    Assert.False(result.IsValid);
    Assert.NotNull(result.Error);
    Assert.Contains("Unclosed '['", result.Error);
  }

  // ── Empty / malformed filter expression ──────────────────────────────────

  [Theory]
  [InlineData("$.items[?()]")]
  [InlineData("$.items[?( )]")]
  [InlineData("$.items[?(   )]")]
  public void Validate_EmptyFilterExpression_ReturnsInvalidWithError(string path)
  {
    var result = JsonPathValidator.Validate(path);

    Assert.False(result.IsValid);
    Assert.NotNull(result.Error);
    Assert.Contains("Empty filter expression", result.Error);
  }

  [Fact]
  public void Validate_FilterMissingClosingParen_ReturnsInvalidWithError()
  {
    var result = JsonPathValidator.Validate("$.items[?(@.isbn]");

    Assert.False(result.IsValid);
    Assert.NotNull(result.Error);
    Assert.Contains("Malformed filter expression", result.Error);
    Assert.Contains("missing closing ')'", result.Error);
  }

  // ── Empty / malformed computed index expression ───────────────────────────

  [Theory]
  [InlineData("$.items[()]")]
  [InlineData("$.items[( )]")]
  [InlineData("$.items[(   )]")]
  public void Validate_EmptyComputedIndexExpression_ReturnsInvalidWithError(string path)
  {
    var result = JsonPathValidator.Validate(path);

    Assert.False(result.IsValid);
    Assert.NotNull(result.Error);
    Assert.Contains("Empty computed index expression", result.Error);
  }

  [Fact]
  public void Validate_ComputedIndexMissingClosingParen_ReturnsInvalidWithError()
  {
    var result = JsonPathValidator.Validate("$.items[(@.length-1]");

    Assert.False(result.IsValid);
    Assert.NotNull(result.Error);
    Assert.Contains("Malformed computed index expression", result.Error);
    Assert.Contains("missing closing ')'", result.Error);
  }

  // ── Error position is reported ────────────────────────────────────────────

  [Fact]
  public void Validate_UnclosedBracket_ErrorIncludesPosition()
  {
    // $.obj[p2,p1 — [ is at index 5 in the original string
    var result = JsonPathValidator.Validate("$.obj[p2,p1");

    Assert.False(result.IsValid);
    Assert.Contains("position 5", result.Error);
  }

  [Fact]
  public void Validate_EmptyFilter_ErrorIncludesPosition()
  {
    // $.items[?()] — [ is at index 7
    var result = JsonPathValidator.Validate("$.items[?()]");

    Assert.False(result.IsValid);
    Assert.Contains("position 7", result.Error);
  }

  // ── IsValid convenience helper ────────────────────────────────────────────

  [Theory]
  [InlineData("$.obj[p2,p1")]
  [InlineData("$.items[?()]")]
  [InlineData("$.items[()]")]
  public void IsValid_InvalidPath_ReturnsFalse(string path)
  {
    Assert.False(JsonPathValidator.IsValid(path));
  }

  // ── Quoted string with ] inside does not confuse the validator ─────────────

  [Fact]
  public void Validate_FilterWithBracketInsideQuotedString_ReturnsValid()
  {
    var result = JsonPathValidator.Validate("$.items[?(@.name == \"[bracket]\")]");

    Assert.True(result.IsValid, result.Error);
  }

  [Fact]
  public void Validate_FilterWithSingleQuotedBracketInsideString_ReturnsValid()
  {
    var result = JsonPathValidator.Validate("$.items[?(@.name == '[bracket]')]");

    Assert.True(result.IsValid, result.Error);
  }
}
