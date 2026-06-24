using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathFilterEvaluator
{
  private static readonly string[] ComparisonOperators = ["==", "!=", "<=", ">=", "<", ">"];

  public static bool Evaluate(JsonNode? candidate, string expression)
  {
    if (string.IsNullOrEmpty(expression))
      return false;

    return EvaluateSpan(candidate, expression.AsSpan().Trim());
  }

  private static bool EvaluateSpan(JsonNode? candidate, ReadOnlySpan<char> expr)
  {
    if (expr.IsEmpty)
      return false;

    var orIndex = FindTopLevelOperator(expr, "||");
    if (orIndex >= 0)
      return EvaluateSpan(candidate, expr[..orIndex]) || EvaluateSpan(candidate, expr[(orIndex + 2)..]);

    var andIndex = FindTopLevelOperator(expr, "&&");
    if (andIndex >= 0)
      return EvaluateSpan(candidate, expr[..andIndex]) && EvaluateSpan(candidate, expr[(andIndex + 2)..]);

    if (expr[0] == '!')
      return !EvaluateSpan(candidate, expr[1..]);

    foreach (var op in ComparisonOperators)
    {
      var idx = FindTopLevelOperator(expr, op);
      if (idx < 0)
        continue;

      var leftSpan = expr[..idx].Trim();
      var rightSpan = expr[(idx + op.Length)..].Trim();

      var leftValue = ResolveOperandSpan(candidate, leftSpan, out var leftExists);
      var rightValue = ResolveOperandSpan(candidate, rightSpan, out var rightExists);
      if (!leftExists || !rightExists)
        return false;

      return Compare(leftValue, rightValue, op);
    }

    _ = ResolveOperandSpan(candidate, expr, out var exists);
    return exists;
  }

  private static int FindTopLevelOperator(ReadOnlySpan<char> expression, ReadOnlySpan<char> op)
  {
    var quote = '\0';
    var depth = 0;

    for (var i = 0; i <= expression.Length - op.Length; i++)
    {
      var ch = expression[i];
      if (quote == '\0')
      {
        if (ch is '"' or '\'')
        {
          quote = ch;
          continue;
        }

        if (ch == '(')
        {
          depth++;
          continue;
        }

        if (ch == ')')
        {
          depth = Math.Max(0, depth - 1);
          continue;
        }

        if (depth == 0 && expression.Slice(i, op.Length).SequenceEqual(op))
          return i;
      }
      else if (ch == quote)
      {
        quote = '\0';
      }
    }

    return -1;
  }

  private static object? ResolveOperandSpan(JsonNode? candidate, ReadOnlySpan<char> token, out bool exists)
  {
    if (token.StartsWith("@."))
      return ResolvePath(candidate, token[2..].ToString(), out exists);

    if (token.SequenceEqual("@"))
    {
      exists = candidate is not null;
      return ToPrimitive(candidate);
    }

    if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
    {
      exists = true;
      return token[1..^1].ToString();
    }

    if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
    {
      exists = true;
      return token[1..^1].ToString();
    }

    if (MemoryExtensions.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return true;
    }

    if (MemoryExtensions.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return false;
    }

    if (MemoryExtensions.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return null;
    }

    // Fast guard: decimal.TryParse is expensive — skip it for tokens that cannot be numbers.
    if (token.IsEmpty || !IsNumberStart(token[0]))
    {
      exists = false;
      return null;
    }

    // decimal.TryParse requires a string — allocate only when needed.
    if (decimal.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
    {
      exists = true;
      return number;
    }

    exists = false;
    return null;
  }

  private static object? ResolveOperand(JsonNode? candidate, string token, out bool exists)
  {
    token = token.Trim();

    if (token.StartsWith("@.", StringComparison.Ordinal))
      return ResolvePath(candidate, token[2..], out exists);

    if (token.Equals("@", StringComparison.Ordinal))
    {
      exists = candidate is not null;
      return ToPrimitive(candidate);
    }

    if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
    {
      exists = true;
      return token[1..^1];
    }

    if (token.StartsWith('\'') && token.EndsWith('\'') && token.Length >= 2)
    {
      exists = true;
      return token[1..^1];
    }

    if (token.Equals("true", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return true;
    }

    if (token.Equals("false", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return false;
    }

    if (token.Equals("null", StringComparison.OrdinalIgnoreCase))
    {
      exists = true;
      return null;
    }

    // Fast guard: decimal.TryParse is expensive — skip it for tokens that cannot be numbers.
    if (token.Length > 0 && !IsNumberStart(token[0]))
    {
      exists = false;
      return null;
    }

    if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
    {
      exists = true;
      return number;
    }

    exists = false;
    return null;
  }

  private static object? ResolvePath(JsonNode? candidate, string path, out bool exists)
  {
    JsonNode? current = candidate;
    exists = current is not null;

    var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var part in parts)
    {
      if (current is JsonObject obj)
      {
        if (!obj.TryGetPropertyValue(part, out current))
        {
          exists = false;
          return null;
        }

        continue;
      }

      exists = false;
      return null;
    }

    exists = current is not null;
    return ToPrimitive(current);
  }

  private static object? ToPrimitive(JsonNode? node)
  {
    if (node is JsonValue value)
    {
      if (value.TryGetValue<bool>(out var boolValue))
        return boolValue;
      if (value.TryGetValue<int>(out var intValue))
        return intValue;
      if (value.TryGetValue<long>(out var longValue))
        return longValue;
      if (value.TryGetValue<decimal>(out var decimalValue))
        return decimalValue;
      if (value.TryGetValue<double>(out var doubleValue))
        return doubleValue;
      if (value.TryGetValue<string>(out var stringValue))
        return stringValue;

      if (value.TryGetValue<JsonElement>(out var element))
      {
        return element.ValueKind switch
        {
          JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
          JsonValueKind.Number when element.TryGetDouble(out var number) => number,
          JsonValueKind.String => element.GetString(),
          JsonValueKind.True => true,
          JsonValueKind.False => false,
          JsonValueKind.Null => null,
          _ => node
        };
      }
    }

    return node;
  }

  private static bool Compare(object? left, object? right, string op)
  {
    if (TryToDecimal(left, out var ln) && TryToDecimal(right, out var rn))
      return CompareNumbers(ln, rn, op);

    if (left is bool lb && right is bool rb)
      return op switch
      {
        "==" => lb == rb,
        "!=" => lb != rb,
        _ => false
      };

    if (left is null || right is null)
      return op switch
      {
        "==" => left is null && right is null,
        "!=" => left is null || right is null,
        _ => false
      };

    var leftString = left.ToString();
    var rightString = right.ToString();
    var cmp = string.Compare(leftString, rightString, StringComparison.Ordinal);

    return op switch
    {
      "==" => cmp == 0,
      "!=" => cmp != 0,
      "<" => cmp < 0,
      "<=" => cmp <= 0,
      ">" => cmp > 0,
      ">=" => cmp >= 0,
      _ => false
    };
  }

  private static bool CompareNumbers(decimal left, decimal right, string op)
    => op switch
    {
      "==" => left == right,
      "!=" => left != right,
      "<" => left < right,
      "<=" => left <= right,
      ">" => left > right,
      ">=" => left >= right,
      _ => false
    };

  private static bool TryToDecimal(object? value, out decimal number)
  {
    switch (value)
    {
      case byte v:
        number = v;
        return true;
      case sbyte v:
        number = v;
        return true;
      case short v:
        number = v;
        return true;
      case ushort v:
        number = v;
        return true;
      case int v:
        number = v;
        return true;
      case uint v:
        number = v;
        return true;
      case long v:
        number = v;
        return true;
      case ulong v:
        number = v;
        return true;
      case float v:
        number = (decimal)v;
        return true;
      case double v:
        number = (decimal)v;
        return true;
      case decimal v:
        number = v;
        return true;
      case string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
        number = parsed;
        return true;
      default:
        number = 0;
        return false;
    }
  }

  private static bool IsNumberStart(char c)
    => c == '-' || (c >= '0' && c <= '9');
}
