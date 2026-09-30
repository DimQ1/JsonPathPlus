using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal enum JsonPathFilterOp
{
  Equal,
  NotEqual,
  LessThan,
  LessThanOrEqual,
  GreaterThan,
  GreaterThanOrEqual
}

/// <summary>
/// A pre-compiled JSONPath filter expression. The filter text is parsed once,
/// then evaluated against many candidates without re-parsing or allocating.
/// </summary>
internal abstract class JsonPathFilterNode
{
  private static readonly (JsonPathFilterOp Op, string Text)[] ComparisonOperators =
  {
    (JsonPathFilterOp.Equal, "=="),
    (JsonPathFilterOp.NotEqual, "!="),
    (JsonPathFilterOp.LessThanOrEqual, "<="),
    (JsonPathFilterOp.GreaterThanOrEqual, ">="),
    (JsonPathFilterOp.LessThan, "<"),
    (JsonPathFilterOp.GreaterThan, ">")
  };

  public abstract bool Evaluate(JsonNode? candidate);

  public static JsonPathFilterNode Compile(string expression)
    => CompileSpan(expression.AsSpan().Trim());

  private static JsonPathFilterNode CompileSpan(ReadOnlySpan<char> expr)
  {
    if (expr.IsEmpty)
      return JsonPathFilterConstant.False;

    var orIndex = FindTopLevelOperator(expr, "||");
    if (orIndex >= 0)
      return new JsonPathOrNode(CompileSpan(expr[..orIndex]), CompileSpan(expr[(orIndex + 2)..]));

    var andIndex = FindTopLevelOperator(expr, "&&");
    if (andIndex >= 0)
      return new JsonPathAndNode(CompileSpan(expr[..andIndex]), CompileSpan(expr[(andIndex + 2)..]));

    if (expr[0] == '!')
      return new JsonPathNotNode(CompileSpan(expr[1..]));

    foreach (var (op, text) in ComparisonOperators)
    {
      var index = FindTopLevelOperator(expr, text);
      if (index < 0)
        continue;

      var left = CompileOperand(expr[..index].Trim());
      var right = CompileOperand(expr[(index + text.Length)..].Trim());
      return new JsonPathCompareNode(left, right, op);
    }

    return new JsonPathExistsNode(CompileOperand(expr));
  }

  private static JsonPathFilterOperand CompileOperand(ReadOnlySpan<char> token)
  {
    if (token.StartsWith("@.", StringComparison.Ordinal))
      return new JsonPathOperand(token[2..]);

    if (token.SequenceEqual("@"))
      return JsonPathCurrentOperand.Instance;

    if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
      return new JsonPathLiteralOperand(token[1..^1].ToString());

    if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
      return new JsonPathLiteralOperand(token[1..^1].ToString());

    if (MemoryExtensions.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(true);

    if (MemoryExtensions.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(false);

    if (MemoryExtensions.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(null);

    if (token.IsEmpty || !IsNumberStart(token[0]))
      return JsonPathInvalidOperand.Instance;

    if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
      return new JsonPathLiteralOperand(number);

    return JsonPathInvalidOperand.Instance;
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

  private static bool IsNumberStart(char c)
    => c == '-' || (c >= '0' && c <= '9');

  private sealed class JsonPathOrNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterNode _left;
    private readonly JsonPathFilterNode _right;

    public JsonPathOrNode(JsonPathFilterNode left, JsonPathFilterNode right)
    {
      _left = left;
      _right = right;
    }

    public override bool Evaluate(JsonNode? candidate)
      => _left.Evaluate(candidate) || _right.Evaluate(candidate);
  }

  private sealed class JsonPathAndNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterNode _left;
    private readonly JsonPathFilterNode _right;

    public JsonPathAndNode(JsonPathFilterNode left, JsonPathFilterNode right)
    {
      _left = left;
      _right = right;
    }

    public override bool Evaluate(JsonNode? candidate)
      => _left.Evaluate(candidate) && _right.Evaluate(candidate);
  }

  private sealed class JsonPathNotNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterNode _child;

    public JsonPathNotNode(JsonPathFilterNode child)
      => _child = child;

    public override bool Evaluate(JsonNode? candidate)
      => !_child.Evaluate(candidate);
  }

  private sealed class JsonPathExistsNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterOperand _operand;

    public JsonPathExistsNode(JsonPathFilterOperand operand)
      => _operand = operand;

    public override bool Evaluate(JsonNode? candidate)
      => _operand.TryResolve(candidate, out _);
  }

  private sealed class JsonPathCompareNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterOperand _left;
    private readonly JsonPathFilterOperand _right;
    private readonly JsonPathFilterOp _op;

    public JsonPathCompareNode(JsonPathFilterOperand left, JsonPathFilterOperand right, JsonPathFilterOp op)
    {
      _left = left;
      _right = right;
      _op = op;
    }

    public override bool Evaluate(JsonNode? candidate)
    {
      if (!_left.TryResolve(candidate, out var left))
        return false;

      if (!_right.TryResolve(candidate, out var right))
        return false;

      return JsonPathFilterEvaluator.CompareValues(left, right, _op);
    }
  }

  private sealed class JsonPathFilterConstant : JsonPathFilterNode
  {
    public static readonly JsonPathFilterConstant False = new();

    public override bool Evaluate(JsonNode? candidate)
      => false;
  }

  private abstract class JsonPathFilterOperand
  {
    public abstract bool TryResolve(JsonNode? candidate, out object? value);
  }

  private sealed class JsonPathOperand : JsonPathFilterOperand
  {
    private readonly string[] _parts;

    public JsonPathOperand(ReadOnlySpan<char> path)
      => _parts = path.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override bool TryResolve(JsonNode? candidate, out object? value)
    {
      JsonNode? current = candidate;

      foreach (var part in _parts)
      {
        if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current))
        {
          value = null;
          return false;
        }
      }

      value = JsonPathFilterEvaluator.ToPrimitive(current);
      return current is not null;
    }
  }

  private sealed class JsonPathCurrentOperand : JsonPathFilterOperand
  {
    public static readonly JsonPathCurrentOperand Instance = new();

    public override bool TryResolve(JsonNode? candidate, out object? value)
    {
      value = JsonPathFilterEvaluator.ToPrimitive(candidate);
      return candidate is not null;
    }
  }

  private sealed class JsonPathLiteralOperand : JsonPathFilterOperand
  {
    private readonly object? _value;

    public JsonPathLiteralOperand(object? value)
      => _value = value;

    public override bool TryResolve(JsonNode? candidate, out object? value)
    {
      value = _value;
      return true;
    }
  }

  private sealed class JsonPathInvalidOperand : JsonPathFilterOperand
  {
    public static readonly JsonPathInvalidOperand Instance = new();

    public override bool TryResolve(JsonNode? candidate, out object? value)
    {
      value = null;
      return false;
    }
  }
}
