using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal static class JsonPathExtractionCore
{
  public static List<JsonPathSegment> ParseSegments(string? selectToken)
    => selectToken is null
      ? new List<JsonPathSegment>()
      : JsonPathParser.Parse(selectToken);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0)
      return root;

    var matches = JsonPathMatcher.FindMatches(root, segments);
    return matches.Count > 0 ? matches[0] : null;
  }

  public static IEnumerable<JsonNode?> FindAllMatches(JsonNode? root, List<JsonPathSegment> segments)
  {
    if (segments.Count == 0)
    {
      yield return root;
      yield break;
    }

    foreach (var match in JsonPathMatcher.FindMatches(root, segments))
      yield return match;
  }

  public static IEnumerable<JsonPathMatch> FindAllMatchesWithPaths(
    JsonNode? root,
    List<JsonPathSegment> segments,
    string rootPath = "$")
  {
    if (segments.Count == 0)
    {
      yield return new JsonPathMatch(rootPath, root);
      yield break;
    }

    foreach (var match in JsonPathMatcher.FindMatchesWithPaths(root, segments, rootPath))
      yield return match;
  }
}
