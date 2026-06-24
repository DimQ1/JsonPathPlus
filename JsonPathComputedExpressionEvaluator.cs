using System;
using System.Collections.Generic;
using System.Globalization;

namespace JsonPathPlus;

internal static class JsonPathComputedExpressionEvaluator
{
  public static bool TryEvaluateIndex(int arrayLength, string expression, out int index)
  {
    index = 0;

    var tokens = TokenizeExpression(expression.AsSpan());
    if (tokens.Count == 0)
      return false;

    if (!TryResolveOperandSpan(tokens[0], arrayLength, out var current))
      return false;

    for (var i = 1; i < tokens.Count; i += 2)
    {
      if (i + 1 >= tokens.Count)
        return false;

      var op = tokens[i];
      if (!TryResolveOperandSpan(tokens[i + 1], arrayLength, out var right))
        return false;

      current = op switch
      {
        "+" => current + right,
        "-" => current - right,
        "*" => current * right,
        "/" when right != 0 => current / right,
        _ => double.NaN
      };

      if (double.IsNaN(current) || double.IsInfinity(current))
        return false;
    }

    index = (int)Math.Truncate(current);
    return true;
  }

  /// <summary>
  /// Tokenizes a simple arithmetic expression without allocating intermediate strings for the Replace+Split steps.
  /// </summary>
  private static List<string> TokenizeExpression(ReadOnlySpan<char> expression)
  {
    var tokens = new List<string>(3);
    var start = 0;

    for (var i = 0; i < expression.Length; i++)
    {
      if (IsOperator(expression[i]))
      {
        if (i > start)
        {
          var operand = expression[start..i].Trim();
          if (!operand.IsEmpty)
            tokens.Add(operand.ToString());
        }

        tokens.Add(expression.Slice(i, 1).ToString());
        start = i + 1;
      }
    }

    if (start < expression.Length)
    {
      var operand = expression[start..].Trim();
      if (!operand.IsEmpty)
        tokens.Add(operand.ToString());
    }

    return tokens;
  }

  private static bool IsOperator(char c)
    => c is '+' or '-' or '*' or '/';

  private static bool TryResolveOperandSpan(ReadOnlySpan<char> token, int arrayLength, out double value)
  {
    if (MemoryExtensions.Equals(token, "@.length", StringComparison.Ordinal))
    {
      value = arrayLength;
      return true;
    }

    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
      return true;

    value = 0;
    return false;
  }

  // Kept for backward compatibility with any external callers.
  private static bool TryResolveOperand(string token, int arrayLength, out double value)
  {
    if (token.Equals("@.length", StringComparison.Ordinal))
    {
      value = arrayLength;
      return true;
    }

    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
      return true;

    value = 0;
    return false;
  }
}
