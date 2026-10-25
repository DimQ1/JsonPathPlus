using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal static partial class JsonPathStreamingMatcher
{
    internal enum RootContainerKind { None, Array, Object }

    public readonly record struct StreamHead
    {
        public RootContainerKind RootKind { get; init; }
        public byte[]? HeadBytes { get; init; }
        public bool IsEmpty => RootKind == RootContainerKind.None;
    }

    public static StreamHead? CanUseStreaming(Stream stream, List<JsonPathSegment> segments)
    {
        if (segments.Count == 0)
            return null;

        var head = PeekStreamHead(stream);
        return CanStream(head.RootKind, segments[0]) ? head : null;
    }

    internal static bool CanStream(RootContainerKind rootKind, JsonPathSegment first)
      => rootKind switch
      {
          RootContainerKind.Array => CanStreamArray(first)
          || first.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ArrayRange
            or JsonPathSegmentType.ArrayUnion or JsonPathSegmentType.ComputedIndex
            or JsonPathSegmentType.RecursiveDescent or JsonPathSegmentType.FieldProjection
            or JsonPathSegmentType.FieldExclusion or JsonPathSegmentType.FieldExistence or JsonPathSegmentType.FieldCount,
          RootContainerKind.Object => first.SegmentType switch
          {
              JsonPathSegmentType.Property => !string.IsNullOrWhiteSpace(first.PropertyName),
              JsonPathSegmentType.Wildcard => true,
              JsonPathSegmentType.PropertyUnion => first.PropertyUnionNames is { Length: > 0 },
              JsonPathSegmentType.Filter or JsonPathSegmentType.RecursiveDescent
              or JsonPathSegmentType.FieldProjection or JsonPathSegmentType.FieldExclusion
              or JsonPathSegmentType.FieldExistence or JsonPathSegmentType.FieldCount
              or JsonPathSegmentType.NestedQuery => true,
              _ => false
          },
          _ => false
      };

    private static bool CanStreamArray(JsonPathSegment segment)
      => segment.SegmentType switch
      {
          JsonPathSegmentType.Wildcard => true,
          JsonPathSegmentType.ArrayIndex => segment.ArrayIndex >= 0,
          JsonPathSegmentType.ArrayRange => segment.ArrayIndex >= 0 && segment.ArrayRangeEnd >= 0,
          JsonPathSegmentType.ArrayUnion => AllNonNegative(segment.ArrayUnionIndices),
          JsonPathSegmentType.Filter => !string.IsNullOrWhiteSpace(segment.FilterExpression),
          _ => false
      };

    private static bool AllNonNegative(int[]? indices)
    {
        if (indices is null)
            return false;
        foreach (var index in indices)
        {
            if (index < 0)
                return false;
        }
        return true;
    }

    public static async Task<JsonNode?> ExtractFirstMatchAsync(
      Stream stream, List<JsonPathSegment> segments, StreamHead head, CancellationToken cancellationToken = default,
      JsonPathExtractionOptions options = default)
    {
        await foreach (var match in ExtractAsync(stream, segments, head, false, cancellationToken, options))
            return match.Value;
        return null;
    }

    public static async IAsyncEnumerable<JsonNode?> ExtractAllMatchesAsync(
      Stream stream, List<JsonPathSegment> segments, StreamHead head,
      [EnumeratorCancellation] CancellationToken cancellationToken = default, JsonPathExtractionOptions options = default)
    {
        await foreach (var match in ExtractAsync(stream, segments, head, false, cancellationToken, options))
            yield return match.Value;
    }

    public static async IAsyncEnumerable<JsonPathMatch> ExtractAllMatchesWithPathsAsync(
      Stream stream, List<JsonPathSegment> segments, StreamHead head,
      [EnumeratorCancellation] CancellationToken cancellationToken = default, JsonPathExtractionOptions options = default)
    {
        await foreach (var match in ExtractAsync(stream, segments, head, true, cancellationToken, options))
            yield return new JsonPathMatch(match.Path, match.Value);
    }

    private static async IAsyncEnumerable<JsonPathValueMatch> ExtractAsync(
      Stream stream, List<JsonPathSegment> segments, StreamHead head, bool withPaths,
      [EnumeratorCancellation] CancellationToken cancellationToken, JsonPathExtractionOptions options)
    {
        if (head.IsEmpty)
            yield break;

        var origin = stream.CanSeek ? stream.Position : -1;
        using var prefixed = head.HeadBytes is { Length: > 0 } ? new PrefixReadStream(head.HeadBytes, stream) : null;
        using var reader = new JsonPathStreamReader(prefixed ?? stream, cancellationToken, options);
        try
        {
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                await foreach (var match in TraverseAsync(reader, segments, 0, withPaths ? JsonPathLocation.Root : null, cancellationToken))
                    yield return match;
            }
        }
        finally
        {
            if (origin >= 0)
                stream.Position = origin;
        }
    }

    private static async IAsyncEnumerable<JsonPathValueMatch> TraverseAsync(
      JsonPathStreamReader reader, List<JsonPathSegment> segments, int segmentIndex, JsonPathLocation? path,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (segmentIndex >= segments.Count)
        {
            using var document = await reader.ReadValueAsync().ConfigureAwait(false);
            yield return new JsonPathValueMatch(path?.ToString() ?? "$", JsonPathElementMatcher.Materialize(document.RootElement));
            yield break;
        }

        var segment = segments[segmentIndex];
        if ((segment.SegmentType is JsonPathSegmentType.Property or JsonPathSegmentType.PropertyUnion
              && reader.TokenType != JsonTokenType.StartObject)
            || (segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ArrayRange
              or JsonPathSegmentType.ArrayUnion or JsonPathSegmentType.ComputedIndex
              && reader.TokenType != JsonTokenType.StartArray)
            || (segment.SegmentType is JsonPathSegmentType.Wildcard or JsonPathSegmentType.Filter
              && reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject)))
        {
            await reader.SkipValueAsync().ConfigureAwait(false);
            yield break;
        }

        if (reader.TokenType == JsonTokenType.StartArray && CanStreamArray(segment)
            && (segmentIndex == 0 || segment.SegmentType != JsonPathSegmentType.ArrayUnion))
        {
            var index = 0;
            var filter = segment.SegmentType == JsonPathSegmentType.Filter
              ? segment.FilterNode ?? JsonPathFilterNode.Compile(segment.FilterExpression!)
              : null;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    yield break;

                var selected = segment.SegmentType switch
                {
                    JsonPathSegmentType.ArrayIndex => index == segment.ArrayIndex,
                    JsonPathSegmentType.ArrayRange => index >= segment.ArrayIndex && index < segment.ArrayRangeEnd,
                    JsonPathSegmentType.ArrayUnion => Array.IndexOf(segment.ArrayUnionIndices!, index) >= 0,
                    _ => true
                };

                if (!selected)
                {
                    await reader.SkipValueAsync().ConfigureAwait(false);
                }
                else if (filter is not null)
                {
                    await foreach (var match in FilterAsync(reader, filter, segments, segmentIndex + 1,
                      path is null ? null : JsonPathElementMatcher.ArrayPath(path, index), cancellationToken))
                        yield return match;
                }
                else
                {
                    await foreach (var match in TraverseAsync(reader, segments, segmentIndex + 1,
                      path is null ? null : JsonPathElementMatcher.ArrayPath(path, index), cancellationToken))
                        yield return match;
                }

                index++;
            }
            throw new JsonException("Incomplete JSON array.");
        }

        if (reader.TokenType == JsonTokenType.StartObject
            && segment.SegmentType is JsonPathSegmentType.Property or JsonPathSegmentType.PropertyUnion
              or JsonPathSegmentType.Wildcard or JsonPathSegmentType.Filter
            && (segmentIndex == 0 || segment.SegmentType != JsonPathSegmentType.PropertyUnion))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    yield break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a JSON property.");

                var name = reader.PropertyName!;
                var selected = segment.SegmentType switch
                {
                    JsonPathSegmentType.Property => string.Equals(segment.PropertyName, name, StringComparison.Ordinal),
                    JsonPathSegmentType.PropertyUnion => segment.PropertyUnionNames is not null
                      && Array.IndexOf(segment.PropertyUnionNames, name) >= 0,
                    _ => true
                };
                if (!await reader.ReadAsync().ConfigureAwait(false))
                    throw new JsonException("Missing JSON property value.");

                if (selected && segment.SegmentType == JsonPathSegmentType.Filter)
                {
                    await foreach (var match in FilterAsync(reader,
                      segment.FilterNode ?? JsonPathFilterNode.Compile(segment.FilterExpression ?? string.Empty),
                      segments, segmentIndex + 1,
                      path is null ? null : JsonPathElementMatcher.PropertyPath(path, name), cancellationToken))
                        yield return match;
                }
                else if (selected)
                {
                    await foreach (var match in TraverseAsync(reader, segments, segmentIndex + 1,
                      path is null ? null : JsonPathElementMatcher.PropertyPath(path, name), cancellationToken))
                        yield return match;
                }
                else
                {
                    await reader.SkipValueAsync().ConfigureAwait(false);
                }
            }
            throw new JsonException("Incomplete JSON object.");
        }

        if (reader.TokenType == JsonTokenType.StartArray
            && segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ArrayRange
              or JsonPathSegmentType.ArrayUnion or JsonPathSegmentType.ComputedIndex)
        {
            using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
            var length = value.ArrayLength!.Value;
            var replaySegments = new List<JsonPathSegment>(segments);
            if (segment.SegmentType == JsonPathSegmentType.ArrayUnion)
            {
                foreach (var requested in segment.ArrayUnionIndices ?? Array.Empty<int>())
                {
                    var index = requested < 0 ? length + requested : requested;
                    if (index < 0 || index >= length)
                        continue;
                    replaySegments[segmentIndex] = segment with { SegmentType = JsonPathSegmentType.ArrayIndex, ArrayIndex = index };
                    await foreach (var match in ReplayAsync(value, replaySegments, segmentIndex, path, cancellationToken))
                        yield return match;
                }
            }
            else
            {
                var start = segment.ArrayIndex;
                var end = segment.ArrayRangeEnd;
                if (segment.SegmentType == JsonPathSegmentType.ComputedIndex)
                {
                    if (!JsonPathComputedExpressionEvaluator.TryEvaluateIndex(length, segment.ComputedIndexExpression ?? string.Empty, out start))
                        yield break;
                }
                if (start < 0)
                    start = length + start;
                if (segment.SegmentType == JsonPathSegmentType.ArrayRange)
                {
                    start = Math.Clamp(start, 0, length);
                    end = end == int.MaxValue ? length : Math.Clamp(end < 0 ? length + end : end, 0, length);
                }
                else if (start < 0 || start >= length)
                    yield break;
                replaySegments[segmentIndex] = segment with
                {
                    SegmentType = segment.SegmentType == JsonPathSegmentType.ArrayRange
                    ? JsonPathSegmentType.ArrayRange : JsonPathSegmentType.ArrayIndex,
                    ArrayIndex = start,
                    ArrayRangeEnd = end
                };
                await foreach (var match in ReplayAsync(value, replaySegments, segmentIndex, path, cancellationToken))
                    yield return match;
            }
            yield break;
        }

        if (reader.TokenType == JsonTokenType.StartObject && segment.SegmentType == JsonPathSegmentType.PropertyUnion)
        {
            using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
            var replaySegments = new List<JsonPathSegment>(segments);
            foreach (var name in segment.PropertyUnionNames ?? Array.Empty<string>())
            {
                replaySegments[segmentIndex] = segment with { SegmentType = JsonPathSegmentType.Property, PropertyName = name };
                await foreach (var match in ReplayAsync(value, replaySegments, segmentIndex, path, cancellationToken))
                    yield return match;
            }
            yield break;
        }

        if (segment.SegmentType == JsonPathSegmentType.RecursiveDescent)
        {
            await foreach (var match in RecursiveAsync(reader, segments, segmentIndex, path, cancellationToken))
                yield return match;
            yield break;
        }
        if (segment.SegmentType is JsonPathSegmentType.FieldProjection or JsonPathSegmentType.FieldExclusion)
        {
            await foreach (var match in ProjectAsync(reader, segments, segmentIndex, path, cancellationToken))
                yield return match;
            yield break;
        }
        if (segment.SegmentType is JsonPathSegmentType.FieldCount or JsonPathSegmentType.FieldExistence)
        {
            if (string.IsNullOrWhiteSpace(segment.PropertyName))
            {
                await reader.SkipValueAsync().ConfigureAwait(false);
                yield break;
            }
            var count = await CountFieldsAsync(reader, segment.PropertyName).ConfigureAwait(false);
            JsonNode? result = segment.SegmentType == JsonPathSegmentType.FieldCount
              ? JsonValue.Create(count) : JsonValue.Create(count > 0);
            foreach (var match in MatchNode(result, segments, segmentIndex + 1, path))
                yield return match;
            yield break;
        }
        if (segment.SegmentType == JsonPathSegmentType.NestedQuery)
        {
            var result = await NestedQueryAsync(reader, segment.NestedQueryBranches, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                foreach (var match in MatchNode(result, segments, segmentIndex + 1, path))
                    yield return match;
            }
            yield break;
        }

        using var subtree = await reader.ReadValueAsync().ConfigureAwait(false);
        foreach (var match in JsonPathElementMatcher.FindMatches(subtree.RootElement, segments, segmentIndex, path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return match;
        }
    }

    private static async IAsyncEnumerable<JsonPathValueMatch> FilterAsync(
        JsonPathStreamReader reader, JsonPathFilterNode filter, List<JsonPathSegment> segments, int nextIndex, JsonPathLocation? path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (reader.TryReadSmallDocument(out var document))
        {
            using (document)
            {
                if (filter.Evaluate(document!.RootElement))
                {
                    foreach (var match in JsonPathElementMatcher.FindMatches(document.RootElement, segments, nextIndex, path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return match;
                    }
                }
            }
            yield break;
        }
        var dependencies = new JsonPathFilterDependencies();
        var canProject = filter.CollectDependencies(dependencies);
        using var value = await reader.CaptureValueAsync(canProject ? dependencies : null).ConfigureAwait(false);
        bool accepted;
        if (value.HasFilterCandidate)
            accepted = filter.Evaluate(value.FilterCandidate);
        else if (canProject)
        {
            using var dependencyReader = new JsonPathStreamReader(value.Stream, cancellationToken, value.Options);
            if (!await dependencyReader.ReadAsync().ConfigureAwait(false))
                throw new JsonException("Missing filter candidate.");
            accepted = filter.Evaluate(await dependencies.ReadAsync(dependencyReader).ConfigureAwait(false));
        }
        else
        {
            using var candidate = await JsonDocument.ParseAsync(value.Stream,
              new JsonDocumentOptions { MaxDepth = value.Options.MaxDepth }, cancellationToken).ConfigureAwait(false);
            accepted = filter.Evaluate(candidate.RootElement);
        }
        if (accepted)
        {
            await foreach (var match in ReplayAsync(value, segments, nextIndex, path, cancellationToken))
                yield return match;
        }
    }

    private static async IAsyncEnumerable<JsonPathValueMatch> ReplayAsync(
      JsonPathValueBuffer value, List<JsonPathSegment> segments, int segmentIndex, JsonPathLocation? path,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        value.Rewind();
        using var reader = new JsonPathStreamReader(value.Stream, cancellationToken, value.Options);
        if (!await reader.ReadAsync().ConfigureAwait(false))
            throw new JsonException("Missing captured JSON value.");
        await foreach (var match in TraverseAsync(reader, segments, segmentIndex, path, cancellationToken))
            yield return match;
    }

    internal static StreamHead PeekStreamHead(Stream stream)
    {
        var origin = stream.CanSeek ? stream.Position : -1;
        var bytes = new byte[64];
        var length = 0;
        try
        {
            // Short reads may split the BOM or leave only whitespace in the first read.
            while (length < bytes.Length)
            {
                var read = stream.Read(bytes, length, bytes.Length - length);
                if (read == 0)
                    break;
                length += read;
                if (DetectRoot(bytes.AsSpan(0, length)) != RootContainerKind.None)
                    break;
            }
            return new StreamHead
            {
                RootKind = DetectRoot(bytes.AsSpan(0, length)),
                HeadBytes = origin >= 0 ? null : bytes.AsSpan(0, length).ToArray()
            };
        }
        finally
        {
            if (origin >= 0)
                stream.Position = origin;
        }
    }

    internal static async ValueTask<StreamHead> PeekStreamHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var origin = stream.CanSeek ? stream.Position : -1;
        var bytes = new byte[64];
        var length = 0;
        try
        {
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                length += read;
                if (DetectRoot(bytes.AsSpan(0, length)) != RootContainerKind.None)
                    break;
            }
            return new StreamHead
            {
                RootKind = DetectRoot(bytes.AsSpan(0, length)),
                HeadBytes = origin >= 0 ? null : bytes.AsSpan(0, length).ToArray()
            };
        }
        finally
        {
            if (origin >= 0)
                stream.Position = origin;
        }
    }

    private static RootContainerKind DetectRoot(ReadOnlySpan<byte> bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        for (var i = start; i < bytes.Length; i++)
        {
            if (bytes[i] is 0x20 or 0x09 or 0x0A or 0x0D)
                continue;
            return bytes[i] switch
            {
                0x5B => RootContainerKind.Array,
                0x7B => RootContainerKind.Object,
                _ => RootContainerKind.None
            };
        }
        return RootContainerKind.None;
    }
}
