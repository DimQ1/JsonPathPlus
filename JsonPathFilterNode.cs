using System;
using System.Globalization;
using System.Text.Json;
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
/// then evaluated against many candidates without re-parsing.
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
  public abstract bool Evaluate(JsonElement candidate);
  internal virtual bool CollectDependencies(JsonPathFilterDependencies dependencies) => true;

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
      return new JsonPathLiteralOperand(JsonPathFilterValue.FromString(token[1..^1].ToString()));

    if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
      return new JsonPathLiteralOperand(JsonPathFilterValue.FromString(token[1..^1].ToString()));

    if (MemoryExtensions.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(JsonPathFilterValue.FromBoolean(true));

    if (MemoryExtensions.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(JsonPathFilterValue.FromBoolean(false));

    if (MemoryExtensions.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
      return new JsonPathLiteralOperand(default);

    if (token.IsEmpty || !IsNumberStart(token[0]))
      return JsonPathInvalidOperand.Instance;

    if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
      return new JsonPathLiteralOperand(JsonPathFilterValue.FromNumber(number));

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
    public override bool Evaluate(JsonElement candidate)
      => _left.Evaluate(candidate) || _right.Evaluate(candidate);
    internal override bool CollectDependencies(JsonPathFilterDependencies dependencies)
      => _left.CollectDependencies(dependencies) & _right.CollectDependencies(dependencies);
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
    public override bool Evaluate(JsonElement candidate)
      => _left.Evaluate(candidate) && _right.Evaluate(candidate);
    internal override bool CollectDependencies(JsonPathFilterDependencies dependencies)
      => _left.CollectDependencies(dependencies) & _right.CollectDependencies(dependencies);
  }

  private sealed class JsonPathNotNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterNode _child;

    public JsonPathNotNode(JsonPathFilterNode child)
      => _child = child;

    public override bool Evaluate(JsonNode? candidate)
      => !_child.Evaluate(candidate);
    public override bool Evaluate(JsonElement candidate)
      => !_child.Evaluate(candidate);
    internal override bool CollectDependencies(JsonPathFilterDependencies dependencies)
      => _child.CollectDependencies(dependencies);
  }

  private sealed class JsonPathExistsNode : JsonPathFilterNode
  {
    private readonly JsonPathFilterOperand _operand;

    public JsonPathExistsNode(JsonPathFilterOperand operand)
      => _operand = operand;

    public override bool Evaluate(JsonNode? candidate)
      => _operand.Exists(candidate);
    public override bool Evaluate(JsonElement candidate)
      => _operand.Exists(candidate);
    internal override bool CollectDependencies(JsonPathFilterDependencies dependencies)
      => _operand.CollectDependencies(dependencies, true);
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

      return left.Compare(right, _op);
    }

    public override bool Evaluate(JsonElement candidate)
      => _left.TryResolve(candidate, out var left)
         && _right.TryResolve(candidate, out var right)
         && left.Compare(right, _op);
    internal override bool CollectDependencies(JsonPathFilterDependencies dependencies)
      => _left.CollectDependencies(dependencies, false) & _right.CollectDependencies(dependencies, false);
  }

  private sealed class JsonPathFilterConstant : JsonPathFilterNode
  {
    public static readonly JsonPathFilterConstant False = new();

    public override bool Evaluate(JsonNode? candidate)
      => false;
    public override bool Evaluate(JsonElement candidate)
      => false;
  }

  private abstract class JsonPathFilterOperand
  {
    public abstract bool TryResolve(JsonNode? candidate, out JsonPathFilterValue value);
    public abstract bool TryResolve(JsonElement candidate, out JsonPathFilterValue value);
    public virtual bool Exists(JsonNode? candidate) => TryResolve(candidate, out _);
    public virtual bool Exists(JsonElement candidate) => TryResolve(candidate, out _);
    public virtual bool CollectDependencies(JsonPathFilterDependencies dependencies, bool presenceOnly) => true;
  }

  private sealed class JsonPathOperand : JsonPathFilterOperand
  {
    private readonly string[] _parts;

    public JsonPathOperand(ReadOnlySpan<char> path)
      => _parts = path.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public override bool CollectDependencies(JsonPathFilterDependencies dependencies, bool presenceOnly)
    {
      dependencies.Add(_parts, presenceOnly);
      return true;
    }

    public override bool TryResolve(JsonNode? candidate, out JsonPathFilterValue value)
    {
      JsonNode? current = candidate;

      foreach (var part in _parts)
      {
        if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current))
        {
          value = default;
          return false;
        }
      }

      value = JsonPathFilterValue.FromNode(current);
      return current is not null;
    }

    public override bool TryResolve(JsonElement candidate, out JsonPathFilterValue value)
    {
      var current = candidate;
      foreach (var part in _parts)
      {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
        {
          value = default;
          return false;
        }
      }

      value = JsonPathFilterValue.FromElement(current);
      return current.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    public override bool Exists(JsonNode? candidate)
    {
      var current = candidate;
      foreach (var part in _parts)
      {
        if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current))
          return false;
      }
      return current is not null;
    }

    public override bool Exists(JsonElement candidate)
    {
      var current = candidate;
      foreach (var part in _parts)
      {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
          return false;
      }
      return current.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }
  }

  private sealed class JsonPathCurrentOperand : JsonPathFilterOperand
  {
    public static readonly JsonPathCurrentOperand Instance = new();
    public override bool CollectDependencies(JsonPathFilterDependencies dependencies, bool presenceOnly)
      => presenceOnly;

    public override bool TryResolve(JsonNode? candidate, out JsonPathFilterValue value)
    {
      value = JsonPathFilterValue.FromNode(candidate);
      return candidate is not null;
    }

    public override bool TryResolve(JsonElement candidate, out JsonPathFilterValue value)
    {
      value = JsonPathFilterValue.FromElement(candidate);
      return candidate.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    public override bool Exists(JsonNode? candidate) => candidate is not null;
    public override bool Exists(JsonElement candidate)
      => candidate.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
  }

  private sealed class JsonPathLiteralOperand : JsonPathFilterOperand
  {
    private readonly JsonPathFilterValue _value;

    public JsonPathLiteralOperand(JsonPathFilterValue value)
      => _value = value;

    public override bool TryResolve(JsonNode? candidate, out JsonPathFilterValue value)
    {
      value = _value;
      return true;
    }

    public override bool TryResolve(JsonElement candidate, out JsonPathFilterValue value)
    {
      value = _value;
      return true;
    }
  }

  private sealed class JsonPathInvalidOperand : JsonPathFilterOperand
  {
    public static readonly JsonPathInvalidOperand Instance = new();

    public override bool TryResolve(JsonNode? candidate, out JsonPathFilterValue value)
    {
      value = default;
      return false;
    }

    public override bool TryResolve(JsonElement candidate, out JsonPathFilterValue value)
    {
      value = default;
      return false;
    }
  }
}
