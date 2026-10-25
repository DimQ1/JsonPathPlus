using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal readonly struct JsonPathFilterValue
{
  private enum ValueKind { Null, Number, Double, Boolean, String, ElementString, Node, Element }

  private readonly ValueKind _kind;
  private readonly decimal _number;
  private readonly double _double;
  private readonly bool _boolean;
  private readonly string? _text;
  private readonly JsonNode? _node;
  private readonly JsonElement _element;

  private JsonPathFilterValue(ValueKind kind, decimal number = 0, bool boolean = false,
    string? text = null, JsonNode? node = null, JsonElement element = default, double doubleNumber = 0)
  {
    _kind = kind;
    _number = number;
    _double = doubleNumber;
    _boolean = boolean;
    _text = text;
    _node = node;
    _element = element;
  }

  public static JsonPathFilterValue FromNumber(decimal number) => new(ValueKind.Number, number: number);
  public static JsonPathFilterValue FromBoolean(bool value) => new(ValueKind.Boolean, boolean: value);
  public static JsonPathFilterValue FromString(string value) => new(ValueKind.String, text: value);

  public static JsonPathFilterValue FromNode(JsonNode? node)
  {
    if (node is null)
      return default;

    if (node is JsonValue value)
    {
#if NET9_0_OR_GREATER
      var kind = value.GetValueKind();
#else
      // .NET 8 serializes programmatic JsonValue instances to determine their kind.
      var kind = JsonValueKind.Undefined;
#endif
      if (kind is JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False
          && value.TryGetValue<bool>(out var boolean))
        return FromBoolean(boolean);
      if (kind is JsonValueKind.Undefined or JsonValueKind.Number)
      {
        if (value.TryGetValue<int>(out var integer))
          return FromNumber(integer);
        if (value.TryGetValue<long>(out var longNumber))
          return FromNumber(longNumber);
        if (value.TryGetValue<decimal>(out var number))
          return FromNumber(number);
        if (value.TryGetValue<double>(out var doubleNumber))
          return new JsonPathFilterValue(ValueKind.Double, doubleNumber: doubleNumber);
      }
      if (kind is JsonValueKind.Undefined or JsonValueKind.String && value.TryGetValue<string>(out var text))
        return FromString(text);
      if (value.TryGetValue<JsonElement>(out var element))
        return FromElement(element);
    }

    return new JsonPathFilterValue(ValueKind.Node, node: node);
  }

  public static JsonPathFilterValue FromElement(JsonElement element)
    => element.ValueKind switch
    {
      JsonValueKind.Null or JsonValueKind.Undefined => default,
      JsonValueKind.True => FromBoolean(true),
      JsonValueKind.False => FromBoolean(false),
      JsonValueKind.String => new JsonPathFilterValue(ValueKind.ElementString, element: element),
      JsonValueKind.Number when element.TryGetDecimal(out var number) => FromNumber(number),
      JsonValueKind.Number => new JsonPathFilterValue(ValueKind.Double, doubleNumber: element.GetDouble()),
      _ => new JsonPathFilterValue(ValueKind.Element, element: element)
    };

  public bool Compare(JsonPathFilterValue right, JsonPathFilterOp op)
  {
    if (op is JsonPathFilterOp.Equal or JsonPathFilterOp.NotEqual)
    {
      if (_kind == ValueKind.ElementString && right._kind == ValueKind.String
          && !right.TryGetNumber(out _))
      {
        var equal = _element.ValueEquals(right._text);
        return op == JsonPathFilterOp.Equal ? equal : !equal;
      }
      if (_kind == ValueKind.String && right._kind == ValueKind.ElementString
          && !TryGetNumber(out _))
      {
        var equal = right._element.ValueEquals(_text);
        return op == JsonPathFilterOp.Equal ? equal : !equal;
      }
    }

    if (TryGetNumber(out var leftNumber) && right.TryGetNumber(out var rightNumber))
      return CompareResult(leftNumber.CompareTo(rightNumber), op);

    if (_kind == ValueKind.Boolean && right._kind == ValueKind.Boolean)
      return op switch
      {
        JsonPathFilterOp.Equal => _boolean == right._boolean,
        JsonPathFilterOp.NotEqual => _boolean != right._boolean,
        _ => false
      };

    if (_kind == ValueKind.Null || right._kind == ValueKind.Null)
      return op switch
      {
        JsonPathFilterOp.Equal => _kind == right._kind,
        JsonPathFilterOp.NotEqual => _kind != right._kind,
        _ => false
      };

    return CompareResult(string.CompareOrdinal(GetText(), right.GetText()), op);
  }

  private bool TryGetNumber(out decimal number)
  {
    number = _number;
    if (_kind == ValueKind.Double)
    {
      number = (decimal)_double;
      return true;
    }
    return _kind switch
    {
      ValueKind.Number => true,
      ValueKind.String => decimal.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out number),
      ValueKind.ElementString => decimal.TryParse(_element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
      _ => false
    };
  }

  private string GetText()
    => _kind switch
    {
      ValueKind.Number => _number.ToString(CultureInfo.CurrentCulture),
      ValueKind.Double => _double.ToString(CultureInfo.CurrentCulture),
      ValueKind.Boolean => _boolean.ToString(),
      ValueKind.String => _text!,
      ValueKind.ElementString => _element.GetString()!,
      ValueKind.Node => _node!.ToString(),
      ValueKind.Element => JsonPathElementMatcher.Materialize(_element)!.ToString(),
      _ => string.Empty
    };

  private static bool CompareResult(int comparison, JsonPathFilterOp op)
    => op switch
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
