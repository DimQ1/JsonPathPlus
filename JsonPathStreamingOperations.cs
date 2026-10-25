using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal static partial class JsonPathStreamingMatcher
{
  private static IEnumerable<JsonPathValueMatch> MatchNode(
    JsonNode? node, List<JsonPathSegment> segments, int index, string? path)
  {
    if (path is null)
    {
      foreach (var match in JsonPathExtractionCore.FindAllMatches(node, segments, index))
        yield return new JsonPathValueMatch("$", match);
    }
    else
    {
      foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(node, segments, index, path))
        yield return new JsonPathValueMatch(match.Path, match.Value);
    }
  }

  private static async IAsyncEnumerable<JsonPathValueMatch> RecursiveAsync(
    JsonPathStreamReader reader, List<JsonPathSegment> segments, int segmentIndex, string? path,
    [EnumeratorCancellation] CancellationToken cancellationToken, bool childrenOnly = false)
  {
    var segment = segments[segmentIndex];
    if (reader.TokenType == JsonTokenType.StartArray)
    {
      if (segment.PropertyName is null && !childrenOnly)
      {
        using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
        var selectors = new List<JsonPathSegment>(segments);
        selectors[segmentIndex] = segment with
        {
          SegmentType = segment.ArrayRangeEnd == int.MinValue
            ? JsonPathSegmentType.ArrayIndex : JsonPathSegmentType.ArrayRange
        };
        await foreach (var match in ReplayAsync(value, selectors, segmentIndex, path, cancellationToken))
          yield return match;
        value.Rewind();
        using var replay = new JsonPathStreamReader(value.Stream, cancellationToken, value.Options);
        if (!await replay.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing recursive array.");
        await foreach (var match in RecursiveAsync(replay, segments, segmentIndex, path, cancellationToken, true))
          yield return match;
        yield break;
      }
      var index = 0;
      while (await reader.ReadAsync().ConfigureAwait(false))
      {
        if (reader.TokenType == JsonTokenType.EndArray)
          yield break;
        await foreach (var match in RecursiveAsync(reader, segments, segmentIndex,
          path is null ? null : JsonPathElementMatcher.ArrayPath(path, index), cancellationToken))
          yield return match;
        index++;
      }
      throw new JsonException("Incomplete recursive array.");
    }
    if (reader.TokenType != JsonTokenType.StartObject)
    {
      await reader.SkipValueAsync().ConfigureAwait(false);
      yield break;
    }
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
      if (reader.TokenType == JsonTokenType.EndObject)
        yield break;
      if (reader.TokenType != JsonTokenType.PropertyName)
        throw new JsonException("Expected a JSON property.");
      var name = reader.PropertyName!;
      var childPath = path is null ? null : JsonPathElementMatcher.PropertyPath(path, name);
      if (!await reader.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing JSON property value.");
      if (segment.PropertyName == "*" || name == segment.PropertyName)
      {
        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
          using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
          await foreach (var match in ReplayAsync(value, segments, segmentIndex + 1, childPath, cancellationToken))
            yield return match;
          await foreach (var match in ReplayAsync(value, segments, segmentIndex, childPath, cancellationToken))
            yield return match;
        }
        else
        {
          await foreach (var match in TraverseAsync(reader, segments, segmentIndex + 1, childPath, cancellationToken))
            yield return match;
        }
      }
      else
      {
        await foreach (var match in RecursiveAsync(reader, segments, segmentIndex, childPath, cancellationToken))
          yield return match;
      }
    }
    throw new JsonException("Incomplete recursive object.");
  }

  private static async IAsyncEnumerable<JsonPathValueMatch> ProjectAsync(
    JsonPathStreamReader reader, List<JsonPathSegment> segments, int segmentIndex, string? path,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var segment = segments[segmentIndex];
    if (segment.ProjectionFields is not { Length: > 0 } fields)
    {
      await reader.SkipValueAsync().ConfigureAwait(false);
      yield break;
    }
    var exclude = segment.SegmentType == JsonPathSegmentType.FieldExclusion;
    if (reader.TokenType == JsonTokenType.StartArray)
    {
      var index = 0;
      while (await reader.ReadAsync().ConfigureAwait(false))
      {
        if (reader.TokenType == JsonTokenType.EndArray)
          yield break;
        var result = await ProjectObjectAsync(reader, fields, exclude).ConfigureAwait(false);
        if (result is not null)
        {
          foreach (var match in MatchNode(result, segments, segmentIndex + 1,
            path is null ? null : JsonPathElementMatcher.ArrayPath(path, index)))
          {
            cancellationToken.ThrowIfCancellationRequested();
            yield return match;
          }
        }
        index++;
      }
      throw new JsonException("Incomplete projection array.");
    }
    var projected = await ProjectObjectAsync(reader, fields, exclude).ConfigureAwait(false);
    if (projected is not null)
    {
      foreach (var match in MatchNode(projected, segments, segmentIndex + 1, path))
        yield return match;
    }
  }

  private static async ValueTask<JsonObject?> ProjectObjectAsync(
    JsonPathStreamReader reader, string[] fields, bool exclude)
  {
    if (reader.TokenType != JsonTokenType.StartObject)
    {
      await reader.SkipValueAsync().ConfigureAwait(false);
      return null;
    }
    var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
      if (reader.TokenType == JsonTokenType.EndObject)
      {
        var result = new JsonObject();
        if (exclude)
        {
          foreach (var property in values)
            result[property.Key] = property.Value;
        }
        else
        {
          foreach (var field in fields)
          {
            if (!result.ContainsKey(field) && values.TryGetValue(field, out var value))
              result[field] = value;
          }
        }
        return exclude || result.Count > 0 ? result : null;
      }
      if (reader.TokenType != JsonTokenType.PropertyName)
        throw new JsonException("Expected a JSON property.");
      var name = reader.PropertyName!;
      if (!await reader.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing JSON property value.");
      if ((Array.IndexOf(fields, name) >= 0) != exclude)
      {
        using var document = await reader.ReadValueAsync().ConfigureAwait(false);
        values[name] = JsonPathElementMatcher.Materialize(document.RootElement);
      }
      else
        await reader.SkipValueAsync().ConfigureAwait(false);
    }
    throw new JsonException("Incomplete projection object.");
  }

  private static async ValueTask<int> CountFieldsAsync(JsonPathStreamReader reader, string name)
  {
    var token = reader.TokenType;
    if (token is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
      return 0;
    var count = 0;
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
      if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
        return count;
      if (token == JsonTokenType.StartObject)
      {
        if (reader.TokenType != JsonTokenType.PropertyName)
          throw new JsonException("Expected a JSON property.");
        if (name == "*")
          count = checked(count + 1);
        else if (name == reader.PropertyName)
          count = 1;
        if (!await reader.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing JSON property value.");
        await reader.SkipValueAsync().ConfigureAwait(false);
      }
      else if (name == "*")
      {
        count = checked(count + 1);
        await reader.SkipValueAsync().ConfigureAwait(false);
      }
      else if (reader.TokenType == JsonTokenType.StartObject)
        count = checked(count + await CountFieldsAsync(reader, name).ConfigureAwait(false));
      else
        await reader.SkipValueAsync().ConfigureAwait(false);
    }
    throw new JsonException("Incomplete field-count container.");
  }

  private static async ValueTask<JsonObject?> NestedQueryAsync(
    JsonPathStreamReader reader, NestedQueryBranch[]? branches, CancellationToken cancellationToken)
  {
    if (reader.TokenType != JsonTokenType.StartObject || branches is not { Length: > 0 })
    {
      await reader.SkipValueAsync().ConfigureAwait(false);
      return null;
    }
    using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
    var result = new JsonObject();
    foreach (var branch in branches)
    {
      value.Rewind();
      using var replay = new JsonPathStreamReader(value.Stream, cancellationToken, value.Options);
      if (!await replay.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing nested-query object.");
      JsonNode? first = null;
      JsonArray? multiple = null;
      var count = 0;
      while (await replay.ReadAsync().ConfigureAwait(false))
      {
        if (replay.TokenType == JsonTokenType.EndObject)
          break;
        if (replay.TokenType != JsonTokenType.PropertyName)
          throw new JsonException("Expected a JSON property.");
        var selected = replay.PropertyName == branch.PropertyName;
        if (!await replay.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing JSON property value.");
        if (!selected || replay.TokenType == JsonTokenType.Null)
        {
          await replay.SkipValueAsync().ConfigureAwait(false);
          continue;
        }
        await foreach (var match in TraverseAsync(replay, branch.SubSegments, 0, null, cancellationToken))
        {
          var item = match.Value?.DeepClone();
          if (count == 0)
            first = item;
          else if (count == 1)
            multiple = new JsonArray(first, item);
          else
            multiple!.Add(item);
          count++;
        }
      }
      if (count > 0)
        result[branch.PropertyName] = multiple ?? first;
    }
    return result.Count > 0 ? result : null;
  }
}
