using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathFilterEvaluator
{
  public static bool Evaluate(JsonNode? candidate, string expression)
  {
    if (string.IsNullOrEmpty(expression))
      return false;

    return JsonPathFilterNode.Compile(expression).Evaluate(candidate);
  }

  internal static bool CompareValues(object? left, object? right, JsonPathFilterOp op)
  {
    if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
      return CompareNumbers(leftNumber, rightNumber, op);

    if (left is bool leftBool && right is bool rightBool)
      return op switch
      {
        JsonPathFilterOp.Equal => leftBool == rightBool,
        JsonPathFilterOp.NotEqual => leftBool != rightBool,
        _ => false
      };

    if (left is null || right is null)
      return op switch
      {
        JsonPathFilterOp.Equal => left is null && right is null,
        JsonPathFilterOp.NotEqual => left is null || right is null,
        _ => false
      };

    if (left is string leftString && right is string rightString)
      return CompareStrings(leftString, rightString, op);

    return CompareStrings(left.ToString()!, right.ToString()!, op);
  }

  private static bool CompareStrings(string left, string right, JsonPathFilterOp op)
  {
    var comparison = string.CompareOrdinal(left, right);
    return op switch
    {
      JsonPathFilterOp.Equal => comparison == 0,
      JsonPathFilterOp.NotEqual => comparison != 0,
      JsonPathFilterOp.LessThan => comparison < 0,
      JsonPathFilterOp.LessThanOrEqual => comparison <= 0,
      JsonPathFilterOp.GreaterThan => comparison > 0,
      JsonPathFilterOp.GreaterThanOrEqual => comparison >= 0,
      _ => false
    };
  }

  private static bool CompareNumbers(decimal left, decimal right, JsonPathFilterOp op)
    => op switch
    {
      JsonPathFilterOp.Equal => left == right,
      JsonPathFilterOp.NotEqual => left != right,
      JsonPathFilterOp.LessThan => left < right,
      JsonPathFilterOp.LessThanOrEqual => left <= right,
      JsonPathFilterOp.GreaterThan => left > right,
      JsonPathFilterOp.GreaterThanOrEqual => left >= right,
      _ => false
    };

  internal static object? ToPrimitive(JsonNode? node)
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

  internal static bool TryToDecimal(object? value, out decimal number)
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
}
