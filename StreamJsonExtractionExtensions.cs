using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace JsonPathPlus;

/// <summary>
/// Provides <see cref="Stream"/> extensions for extracting JSON subtrees identified by
/// JSONPath-like token paths.
/// </summary>
public static class StreamJsonExtractionExtensions
{
  /// <summary>
  /// Reads JSON from <paramref name="stream"/> and returns the first match for
  /// <paramref name="selectToken"/>.
  /// <para>
  /// Path syntax supports:
  /// <c>$</c> root token, dot-notation property access, bracket array indexing, wildcards (*),
  /// array ranges ([start:end]), and recursive descent (..).
  /// Examples: <c>"$.document_data.items[0].seal"</c>, <c>"$.items[*].id"</c>, <c>"..propertyName"</c>
  /// </para>
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath-like token path, or <c>null</c>/<c>"$"</c> to return the entire document.</param>
  /// <returns>The <see cref="JsonNode"/> at the specified path (first match for wildcards/recursive),
  /// the entire document when <paramref name="selectToken"/> is <c>null</c> or points to root, or <c>null</c> if not found.</returns>
  public static async Task<JsonNode?> ExtractFirstJsonMatchAsync(this Stream stream, string? selectToken)
  {
    ArgumentNullException.ThrowIfNull(stream);

    var segments = selectToken is null
      ? new List<JsonPathSegment>()
      : JsonPathParser.Parse(selectToken);
    var root = await JsonNode.ParseAsync(stream);
    if (segments.Count == 0)
      return root;

    var matches = JsonPathMatcher.FindMatches(root, segments);
    return matches.Count > 0 ? matches[0] : null;
  }

  /// <summary>
  /// Extracts all JSON subtrees matching <paramref name="selectToken"/>.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath with wildcards or ranges (e.g., <c>"$.items[*].id"</c>, <c>"[1:3]"</c>).</param>
  /// <returns>Async enumerable of matching <see cref="JsonNode"/> values.</returns>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(
    this Stream stream, string? selectToken)
  {
    ArgumentNullException.ThrowIfNull(stream);

    var segments = selectToken is null
      ? new List<JsonPathSegment>()
      : JsonPathParser.Parse(selectToken);
    var root = await JsonNode.ParseAsync(stream);

    if (segments.Count == 0)
    {
      yield return root;
      yield break;
    }

    foreach (var match in JsonPathMatcher.FindMatches(root, segments))
      yield return match;
  }
}
