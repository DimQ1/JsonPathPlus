using System;

namespace JsonPathPlus;

/// <summary>
/// Validates JSONPath-like expressions without executing them against any document.
/// </summary>
public static class JsonPathValidator
{
  private static readonly JsonPathValidationResult ValidResult = new(true);

  /// <summary>
  /// Returns <see langword="true"/> when <paramref name="path"/> is structurally valid.
  /// <para>
  /// <see langword="null"/> and empty strings are considered valid (they select the root document).
  /// </para>
  /// </summary>
  public static bool IsValid(string? path) => Validate(path).IsValid;

  /// <summary>
  /// Validates <paramref name="path"/> and returns a <see cref="JsonPathValidationResult"/>
  /// that contains the result and an optional human-readable <see cref="JsonPathValidationResult.Error"/> message.
  /// </summary>
  public static JsonPathValidationResult Validate(string? path)
  {
    if (string.IsNullOrEmpty(path))
      return ValidResult;

    var span = path.AsSpan();
    var offset = 0;

    if (span[0] == '$')
    {
      span = span[1..];
      offset = 1;
    }

    for (var i = 0; i < span.Length; i++)
    {
      if (span[i] != '[')
        continue;

      var closeIdx = FindMatchingClose(span, i);
      if (closeIdx < 0)
        return new JsonPathValidationResult(false,
          $"Unclosed '[' at position {offset + i}.");

      var inner = span[(i + 1)..closeIdx];
      var innerError = ValidateInner(inner, offset + i);
      if (innerError is not null)
        return innerError;

      i = closeIdx;
    }

    return ValidResult;
  }

  private static JsonPathValidationResult? ValidateInner(ReadOnlySpan<char> inner, int position)
  {
    if (inner.IsEmpty)
      return null;

    // Filter expression: ?(...)
    if (inner[0] == '?' && inner.Length >= 2 && inner[1] == '(')
    {
      if (inner[^1] != ')')
        return new JsonPathValidationResult(false,
          $"Malformed filter expression at position {position}: missing closing ')'.");

      if (inner[2..^1].Trim().IsEmpty)
        return new JsonPathValidationResult(false,
          $"Empty filter expression at position {position}.");

      return null;
    }

    // Computed index expression: (...)
    if (inner[0] == '(')
    {
      if (inner[^1] != ')')
        return new JsonPathValidationResult(false,
          $"Malformed computed index expression at position {position}: missing closing ')'.");

      if (inner[1..^1].Trim().IsEmpty)
        return new JsonPathValidationResult(false,
          $"Empty computed index expression at position {position}.");

      return null;
    }

    return null;
  }

  /// <summary>
  /// Finds the index of the <c>]</c> that closes the <c>[</c> at <paramref name="openPos"/>,
  /// respecting single- and double-quoted strings so that brackets inside string literals
  /// are not mistaken for the closing bracket.
  /// Returns -1 when no matching <c>]</c> exists.
  /// </summary>
  private static int FindMatchingClose(ReadOnlySpan<char> span, int openPos)
  {
    var quote = '\0';
    for (var i = openPos + 1; i < span.Length; i++)
    {
      var ch = span[i];
      if (quote != '\0')
      {
        if (ch == quote) quote = '\0';
        continue;
      }

      if (ch is '"' or '\'') { quote = ch; continue; }
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
