using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

/// <summary>
/// Provides JSON extraction extensions for <see cref="Stream"/>, <see cref="JsonNode"/>, and JSON <see cref="string"/> inputs.
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
  public static async Task<JsonNode?> ExtractFirstJsonMatchAsync(this Stream stream, string? selectToken, CancellationToken cancellationToken = default)
    => await stream.ExtractFirstJsonMatchAsync(selectToken, default, cancellationToken);

  /// <summary>
  /// Reads JSON from <paramref name="stream"/> and returns the first match for
  /// <paramref name="selectToken"/>.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath-like token path, or <c>null</c>/<c>"$"</c> to return the entire document.</param>
  /// <param name="options">Extraction options.</param>
  /// <returns>The <see cref="JsonNode"/> at the specified path (first match for wildcards/recursive),
  /// the entire document when <paramref name="selectToken"/> is <c>null</c> or points to root, or <c>null</c> if not found.</returns>
  public static async Task<JsonNode?> ExtractFirstJsonMatchAsync(
    this Stream stream,
    string? selectToken,
    JsonPathExtractionOptions options,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(stream);
    cancellationToken.ThrowIfCancellationRequested();

    var segments = JsonPathExtractionCore.ParseSegments(selectToken);

    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, segments);
    if (head.HasValue)
      return await JsonPathStreamingMatcher.ExtractFirstMatchAsync(stream, segments, head.Value, cancellationToken);

    EnsureFullParseAllowed(stream, options);

    var root = await JsonNode.ParseAsync(stream, nodeOptions: default, documentOptions: default, cancellationToken).ConfigureAwait(false);
    return JsonPathExtractionCore.FindFirstMatch(root, segments);
  }

  /// <summary>
  /// Extracts all JSON subtrees matching <paramref name="selectToken"/>.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath with wildcards or ranges (e.g., <c>"$.items[*].id"</c>, <c>"[1:3]"</c>).</param>
  /// <returns>Async enumerable of matching <see cref="JsonNode"/> values.</returns>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(
    this Stream stream, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var match in stream.ExtractAllJsonMatchesAsync(selectToken, default, cancellationToken))
      yield return match;
  }

  /// <summary>
  /// Extracts all JSON subtrees matching <paramref name="selectToken"/>.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath with wildcards or ranges (e.g., <c>"$.items[*].id"</c>, <c>"[1:3]"</c>).</param>
  /// <param name="options">Extraction options.</param>
  /// <returns>Async enumerable of matching <see cref="JsonNode"/> values.</returns>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(
    this Stream stream,
    string? selectToken,
    JsonPathExtractionOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(stream);
    cancellationToken.ThrowIfCancellationRequested();

    var segments = JsonPathExtractionCore.ParseSegments(selectToken);

    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, segments);
    if (head.HasValue)
    {
      await foreach (var match in JsonPathStreamingMatcher.ExtractAllMatchesAsync(stream, segments, head.Value, cancellationToken))
        yield return match;
      yield break;
    }

    EnsureFullParseAllowed(stream, options);

    var root = await JsonNode.ParseAsync(stream, nodeOptions: default, documentOptions: default, cancellationToken).ConfigureAwait(false);
    foreach (var match in JsonPathExtractionCore.FindAllMatches(root, segments))
      yield return match;
  }

  /// <summary>
  /// Extracts all JSON subtrees matching <paramref name="selectToken"/> with absolute JSONPath locations.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath with wildcards or ranges.</param>
  /// <returns>Async enumerable of matching values and paths.</returns>
  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllJsonMatchesWithPathsAsync(
    this Stream stream, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var match in stream.ExtractAllJsonMatchesWithPathsAsync(selectToken, default, cancellationToken))
      yield return match;
  }

  /// <summary>
  /// Extracts all JSON subtrees matching <paramref name="selectToken"/> with absolute JSONPath locations.
  /// </summary>
  /// <param name="stream">The JSON stream to read from.</param>
  /// <param name="selectToken">JSONPath with wildcards or ranges.</param>
  /// <param name="options">Extraction options.</param>
  /// <returns>Async enumerable of matching values and paths.</returns>
  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllJsonMatchesWithPathsAsync(
    this Stream stream,
    string? selectToken,
    JsonPathExtractionOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(stream);
    cancellationToken.ThrowIfCancellationRequested();

    var segments = JsonPathExtractionCore.ParseSegments(selectToken);

    var head = JsonPathStreamingMatcher.CanUseStreaming(stream, segments);
    if (head.HasValue)
    {
      await foreach (var match in JsonPathStreamingMatcher.ExtractAllMatchesWithPathsAsync(stream, segments, head.Value, cancellationToken))
        yield return match;
      yield break;
    }

    EnsureFullParseAllowed(stream, options);

    var root = await JsonNode.ParseAsync(stream, nodeOptions: default, documentOptions: default, cancellationToken).ConfigureAwait(false);
    foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(root, segments))
      yield return match;
  }

  private static void EnsureFullParseAllowed(Stream stream, JsonPathExtractionOptions options)
  {
    if (!options.FullParseMaxBytes.HasValue)
      return;

    if (!stream.CanSeek)
      throw new InvalidOperationException("FullParseMaxBytes requires a seekable stream so remaining size can be validated before full parse fallback.");

    var remainingBytes = stream.Length - stream.Position;
    if (remainingBytes > options.FullParseMaxBytes.Value)
    {
      throw new InvalidOperationException(
        $"The query requires full-document parsing of {remainingBytes} bytes, which exceeds FullParseMaxBytes ({options.FullParseMaxBytes.Value} bytes). Use a streamable selector or increase the cap.");
    }
  }

  /// <summary>
  /// Returns the first JSON match for <paramref name="node"/> at <paramref name="selectToken"/>.
  /// </summary>
  public static Task<JsonNode?> ExtractFirstJsonMatchAsync(this JsonNode? node, string? selectToken, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var segments = JsonPathExtractionCore.ParseSegments(selectToken);
    return Task.FromResult(JsonPathExtractionCore.FindFirstMatch(node, segments));
  }

  /// <summary>
  /// Returns all JSON matches for <paramref name="node"/> at <paramref name="selectToken"/>.
  /// </summary>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(
    this JsonNode? node, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var segments = JsonPathExtractionCore.ParseSegments(selectToken);
    foreach (var match in JsonPathExtractionCore.FindAllMatches(node, segments))
    {
      cancellationToken.ThrowIfCancellationRequested();
      yield return match;
    }

    await Task.CompletedTask;
  }

  /// <summary>
  /// Returns all JSON matches for <paramref name="node"/> at <paramref name="selectToken"/> with absolute JSONPath locations.
  /// </summary>
  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllJsonMatchesWithPathsAsync(
    this JsonNode? node, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var segments = JsonPathExtractionCore.ParseSegments(selectToken);
    foreach (var match in JsonPathExtractionCore.FindAllMatchesWithPaths(node, segments))
    {
      cancellationToken.ThrowIfCancellationRequested();
      yield return match;
    }

    await Task.CompletedTask;
  }

  /// <summary>
  /// Parses <paramref name="json"/> and returns the first JSON match at <paramref name="selectToken"/>.
  /// Uses stream-based extraction to avoid materializing the full <see cref="JsonNode"/> tree
  /// for root arrays and objects. The UTF-8 encoding buffer is rented from <see cref="ArrayPool{T}"/>.
  /// </summary>
  public static async Task<JsonNode?> ExtractFirstJsonMatchAsync(this string json, string? selectToken, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(json);

    using var rented = RentUtf8Stream(json);
    return await rented.Stream.ExtractFirstJsonMatchAsync(selectToken, cancellationToken);
  }

  /// <summary>
  /// Parses <paramref name="json"/> and returns all JSON matches at <paramref name="selectToken"/>.
  /// Uses stream-based extraction to avoid materializing the full <see cref="JsonNode"/> tree
  /// for root arrays and objects. The UTF-8 encoding buffer is rented from <see cref="ArrayPool{T}"/>.
  /// </summary>
  public static async IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(
    this string json, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(json);

    using var rented = RentUtf8Stream(json);
    await foreach (var match in rented.Stream.ExtractAllJsonMatchesAsync(selectToken, cancellationToken))
      yield return match;
  }

  /// <summary>
  /// Parses <paramref name="json"/> and returns all JSON matches at <paramref name="selectToken"/> with absolute JSONPath locations.
  /// Uses stream-based extraction to avoid materializing the full <see cref="JsonNode"/> tree
  /// for root arrays and objects. The UTF-8 encoding buffer is rented from <see cref="ArrayPool{T}"/>.
  /// </summary>
  public static async IAsyncEnumerable<JsonPathMatch> ExtractAllJsonMatchesWithPathsAsync(
    this string json, string? selectToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(json);

    using var rented = RentUtf8Stream(json);
    await foreach (var match in rented.Stream.ExtractAllJsonMatchesWithPathsAsync(selectToken, cancellationToken))
      yield return match;
  }

  private static RentedUtf8Stream RentUtf8Stream(string json)
  {
    var maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);
    var rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
    var actualLength = Encoding.UTF8.GetBytes(json.AsSpan(), rented);
    var stream = new ReadOnlyMemoryStream(new ReadOnlyMemory<byte>(rented, 0, actualLength));
    return new RentedUtf8Stream(stream, rented);
  }

  private readonly struct RentedUtf8Stream : IDisposable
  {
    public Stream Stream { get; }
    private readonly byte[]? _rented;

    public RentedUtf8Stream(Stream stream, byte[] rented)
    {
      Stream = stream;
      _rented = rented;
    }

    public void Dispose()
    {
      if (_rented is not null)
        ArrayPool<byte>.Shared.Return(_rented);
    }
  }
}
