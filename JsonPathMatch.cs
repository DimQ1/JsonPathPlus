using System.Text.Json.Nodes;

namespace JsonPathPlus;

/// <summary>
/// Represents a JSONPath match with both value and its absolute JSONPath location.
/// </summary>
/// <param name="Path">Absolute JSONPath to the matched value.</param>
/// <param name="Value">Matched JSON value.</param>
public sealed record JsonPathMatch(string Path, JsonNode? Value);