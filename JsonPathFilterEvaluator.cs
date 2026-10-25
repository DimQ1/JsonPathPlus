using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathFilterEvaluator
{
  public static bool Evaluate(JsonNode? candidate, string expression)
    => !string.IsNullOrEmpty(expression) && JsonPathFilterNode.Compile(expression).Evaluate(candidate);
}
