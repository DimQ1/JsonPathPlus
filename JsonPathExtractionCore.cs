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
    => FindFirstMatch(root, segments, 0);

  public static JsonNode? FindFirstMatch(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    if (startIndex >= segments.Count)
      return root;

    return JsonPathMatcher.FindFirstMatch(root, segments, startIndex);
  }

  public static IEnumerable<JsonNode?> FindAllMatches(JsonNode? root, List<JsonPathSegment> segments)
    => FindAllMatches(root, segments, 0);

  public static IEnumerable<JsonNode?> FindAllMatches(JsonNode? root, List<JsonPathSegment> segments, int startIndex)
  {
    if (startIndex >= segments.Count)
    {
      yield return root;
      yield break;
    }

    foreach (var match in JsonPathMatcher.FindMatches(root, segments, startIndex))
      yield return match;
  }

  public static IEnumerable<JsonPathMatch> FindAllMatchesWithPaths(
    JsonNode? root,
    List<JsonPathSegment> segments,
    string rootPath = "$")
    => FindAllMatchesWithPaths(root, segments, 0, rootPath);

  public static IEnumerable<JsonPathMatch> FindAllMatchesWithPaths(
    JsonNode? root,
    List<JsonPathSegment> segments,
    int startIndex,
    string rootPath = "$")
  {
    if (startIndex >= segments.Count)
    {
      yield return new JsonPathMatch(rootPath, root);
      yield break;
    }

    foreach (var match in JsonPathMatcher.FindMatchesWithPaths(root, segments, startIndex, rootPath))
      yield return match;
  }
}
