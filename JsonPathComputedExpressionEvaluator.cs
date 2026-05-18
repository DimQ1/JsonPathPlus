using System;
using System.Globalization;

namespace JsonPathPlus;

internal static class JsonPathComputedExpressionEvaluator
{
  public static bool TryEvaluateIndex(int arrayLength, string expression, out int index)
  {
    index = 0;

    var tokens = expression
      .Replace("+", " + ", StringComparison.Ordinal)
      .Replace("-", " - ", StringComparison.Ordinal)
      .Replace("*", " * ", StringComparison.Ordinal)
      .Replace("/", " / ", StringComparison.Ordinal)
      .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (tokens.Length == 0)
      return false;

    if (!TryResolveOperand(tokens[0], arrayLength, out var current))
      return false;

    for (var i = 1; i < tokens.Length; i += 2)
    {
      if (i + 1 >= tokens.Length)
        return false;

      var op = tokens[i];
      if (!TryResolveOperand(tokens[i + 1], arrayLength, out var right))
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
