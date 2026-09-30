using System;
using System.Globalization;

namespace JsonPathPlus;

internal static class JsonPathComputedExpressionEvaluator
{
  public static bool TryEvaluateIndex(int arrayLength, string expression, out int index)
  {
    index = 0;

    var expr = expression.AsSpan().Trim();
    if (expr.IsEmpty)
      return false;

    if (!TryReadOperand(ref expr, arrayLength, out var current))
      return false;

    while (true)
    {
      expr = expr.Trim();
      if (expr.IsEmpty)
      {
        index = (int)Math.Truncate(current);
        return true;
      }

      var op = expr[0];
      if (op is not ('+' or '-' or '*' or '/'))
        return false;

      expr = expr[1..];

      if (!TryReadOperand(ref expr, arrayLength, out var right))
        return false;

      current = op switch
      {
        '+' => current + right,
        '-' => current - right,
        '*' => current * right,
        '/' when right != 0 => current / right,
        _ => double.NaN
      };

      if (double.IsNaN(current) || double.IsInfinity(current))
        return false;
    }
  }

  private static bool TryReadOperand(ref ReadOnlySpan<char> expr, int arrayLength, out double value)
  {
    expr = expr.TrimStart();

    if (expr.StartsWith("@.length", StringComparison.Ordinal))
    {
      value = arrayLength;
      expr = expr["@.length".Length..];
      return true;
    }

    var i = 0;
    while (i < expr.Length && !IsOperator(expr[i]))
      i++;

    var number = expr[..i].Trim();
    if (number.IsEmpty)
    {
      value = 0;
      return false;
    }

    expr = expr[i..];
    return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
  }

  private static bool IsOperator(char c)
    => c is '+' or '-' or '*' or '/';
}
