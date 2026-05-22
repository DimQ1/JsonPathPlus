using System;

namespace JsonPathPlus;

/// <summary>
/// Validates JSONPath-like expressions without executing them against any document.
/// </summary>
public static class JsonPathValidator
{
  private static readonly JsonPathValidationResult ValidResult = new(true);

  private enum ValidationErrorKind
  {
    None,
    UnclosedBracket,
    MalformedFilter,
    EmptyFilter,
    MalformedComputedIndex,
    EmptyComputedIndex
  }

  /// <summary>
  /// Returns <see langword="true"/> when <paramref name="path"/> is structurally valid.
  /// <para>
  /// <see langword="null"/> and empty strings are considered valid (they select the root document).
  /// </para>
  /// </summary>
  public static bool IsValid(string? path)
    => string.IsNullOrEmpty(path) || TryValidate(path.AsSpan(), out _, out _);

  /// <summary>
  /// Validates <paramref name="path"/> and returns a <see cref="JsonPathValidationResult"/>
  /// that contains the result and an optional human-readable <see cref="JsonPathValidationResult.Error"/> message.
  /// </summary>
  public static JsonPathValidationResult Validate(string? path)
  {
    if (string.IsNullOrEmpty(path))
      return ValidResult;

    if (TryValidate(path.AsSpan(), out var error, out var position))
      return ValidResult;

    return CreateInvalidResult(error, position);
  }

  private static bool TryValidate(ReadOnlySpan<char> span, out ValidationErrorKind error, out int position)
  {
    error = ValidationErrorKind.None;
    position = -1;

    var offset = 0;

    if (span[0] == '$')
    {
      span = span[1..];
      offset = 1;
    }

    var openIdx = span.IndexOf('[');
    while (openIdx >= 0)
    {
      var closeIdx = FindMatchingClose(span, openIdx);
      if (closeIdx < 0)
      {
        error = ValidationErrorKind.UnclosedBracket;
        position = offset + openIdx;
        return false;
      }

      var inner = span[(openIdx + 1)..closeIdx];
      if (!ValidateInner(inner, offset + openIdx, out error, out position))
        return false;

      var nextStart = closeIdx + 1;
      if (nextStart >= span.Length)
        return true;

      var nextOpen = span[nextStart..].IndexOf('[');
      openIdx = nextOpen < 0 ? -1 : nextStart + nextOpen;
    }

    return true;
  }

  private static bool ValidateInner(ReadOnlySpan<char> inner, int innerPosition, out ValidationErrorKind error, out int position)
  {
    error = ValidationErrorKind.None;
    position = -1;

    if (inner.IsEmpty)
      return true;

    if (inner[0] == '?' && inner.Length >= 2 && inner[1] == '(')
    {
      if (inner[^1] != ')')
      {
        error = ValidationErrorKind.MalformedFilter;
        position = innerPosition;
        return false;
      }

      if (IsWhiteSpace(inner[2..^1]))
      {
        error = ValidationErrorKind.EmptyFilter;
        position = innerPosition;
        return false;
      }

      return true;
    }

    if (inner[0] == '(')
    {
      if (inner[^1] != ')')
      {
        error = ValidationErrorKind.MalformedComputedIndex;
        position = innerPosition;
        return false;
      }

      if (IsWhiteSpace(inner[1..^1]))
      {
        error = ValidationErrorKind.EmptyComputedIndex;
        position = innerPosition;
        return false;
      }

      return true;
    }

    return true;
  }

  private static JsonPathValidationResult CreateInvalidResult(ValidationErrorKind error, int position)
    => error switch
    {
      ValidationErrorKind.UnclosedBracket => new JsonPathValidationResult(false,
        $"Unclosed '[' at position {position}."),
      ValidationErrorKind.MalformedFilter => new JsonPathValidationResult(false,
        $"Malformed filter expression at position {position}: missing closing ')'."),
      ValidationErrorKind.EmptyFilter => new JsonPathValidationResult(false,
        $"Empty filter expression at position {position}."),
      ValidationErrorKind.MalformedComputedIndex => new JsonPathValidationResult(false,
        $"Malformed computed index expression at position {position}: missing closing ')'."),
      ValidationErrorKind.EmptyComputedIndex => new JsonPathValidationResult(false,
        $"Empty computed index expression at position {position}."),
      _ => new JsonPathValidationResult(false, $"Invalid JSONPath expression at position {position}.")
    };

  private static bool IsWhiteSpace(ReadOnlySpan<char> value)
  {
    for (var i = 0; i < value.Length; i++)
    {
      if (!char.IsWhiteSpace(value[i]))
        return false;
    }

    return true;
  }

  /// <summary>
  /// Finds the index of the <c>]</c> that closes the <c>[</c> at <paramref name="openPos"/>,
  /// respecting single- and double-quoted strings so that brackets inside string literals
  /// are not mistaken for the closing bracket.
  /// Returns -1 when no matching <c>]</c> exists.
  /// </summary>
  private static int FindMatchingClose(ReadOnlySpan<char> span, int openPos)
  {
    var firstSpecial = span[(openPos + 1)..].IndexOfAny(']', '"', '\'');
    if (firstSpecial < 0)
      return -1;

    var firstSpecialIndex = openPos + 1 + firstSpecial;
    var firstSpecialChar = span[firstSpecialIndex];
    if (firstSpecialChar == ']')
      return firstSpecialIndex;

    return FindMatchingCloseAfterQuote(span, firstSpecialIndex, firstSpecialChar);
  }

  private static int FindMatchingCloseAfterQuote(ReadOnlySpan<char> span, int quoteStart, char quote)
  {
    var activeQuote = quote;
    for (var i = quoteStart + 1; i < span.Length; i++)
    {
      var ch = span[i];
      if (activeQuote != '\0')
      {
        if (ch == activeQuote)
          activeQuote = '\0';

        continue;
      }

      if (ch is '"' or '\'')
      {
        activeQuote = ch;
        continue;
      }

      if (ch == ']') return i;
    }

    return -1;
  }
}

/// <summary>
/// Represents the outcome of a <see cref="JsonPathValidator.Validate"/> call.
/// </summary>
/// <param name="IsValid">Whether the path is structurally valid.</param>
/// <param name="Error">Human-readable error message when <see cref="IsValid"/> is <see langword="false"/>; otherwise <see langword="null"/>.</param>
public sealed record JsonPathValidationResult(bool IsValid, string? Error = null);
