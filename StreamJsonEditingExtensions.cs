using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

/// <summary>
/// Rewrites JSON streams without constructing a document DOM. Caller-owned streams remain open.
/// Matches are edited once, in source order; a selected ancestor suppresses edits to its descendants.
/// </summary>
public static class StreamJsonEditingExtensions
{
  /// <summary>
  /// Edits each selected value with System.Text.Json.Nodes APIs. Only that value is materialized.
  /// Return null to write JSON null; use RemoveJsonMatchesAsync for deletion.
  /// Selecting the root necessarily materializes the document. Returns the number of selected values.
  /// </summary>
  public static Task<long> EditJsonAsync(this Stream source, Stream destination, string? selectToken,
    Func<JsonNode?, JsonNode?> edit, JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(edit);
    return JsonPathStreamEditor.RewriteAsync(source, destination, selectToken,
      new(JsonPathEditKind.Callback, Callback: edit), options, cancellationToken);
  }

  /// <summary>Replaces existing matches without materializing their original values. Does not create missing paths.</summary>
  public static Task<long> SetJsonValueAsync(this Stream source, Stream destination, string? selectToken,
    JsonNode? value, JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteAsync(source, destination, selectToken,
      new(JsonPathEditKind.Set, Value: value), options, cancellationToken);

  /// <summary>Removes matching properties or array elements. Removing the root document is not supported.</summary>
  public static Task<long> RemoveJsonMatchesAsync(this Stream source, Stream destination, string? selectToken,
    JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteAsync(source, destination, selectToken,
      new(JsonPathEditKind.Remove), options, cancellationToken);

  /// <summary>
  /// Writes a projection back into the document, e.g. $.items[id,name] or $.items[!payload].
  /// Selected objects keep/exclude fields without a DOM. Arrays project direct object items and preserve other items.
  /// Nonselected data is copied. Property order follows the source document.
  /// </summary>
  public static Task<long> ProjectJsonAsync(this Stream source, Stream destination, string projectionPath,
    JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteAsync(source, destination, projectionPath,
      new(JsonPathEditKind.Project), options, cancellationToken);

  /// <summary>
  /// Stages edits in a temporary file, then overwrites from the current position and truncates the stream.
  /// Requires a readable, writable, seekable stream. Preparation failures leave it unchanged.
  /// Final overwrite is not atomic and cannot be cancelled; an I/O failure can leave partial data.
  /// The original position is restored. Only selected callback values are materialized.
  /// </summary>
  public static Task<long> EditJsonInPlaceAsync(this Stream source, string? selectToken,
    Func<JsonNode?, JsonNode?> edit, JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(edit);
    return JsonPathStreamEditor.RewriteInPlaceAsync(source, selectToken,
      new(JsonPathEditKind.Callback, Callback: edit), options, cancellationToken);
  }

  /// <summary>Stages replacements without a source DOM, then overwrites the same seekable stream. See EditJsonInPlaceAsync.</summary>
  public static Task<long> SetJsonValueInPlaceAsync(this Stream source, string? selectToken,
    JsonNode? value, JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteInPlaceAsync(source, selectToken,
      new(JsonPathEditKind.Set, Value: value), options, cancellationToken);

  /// <summary>Stages removals, then overwrites and truncates the same seekable stream. See EditJsonInPlaceAsync.</summary>
  public static Task<long> RemoveJsonMatchesInPlaceAsync(this Stream source, string? selectToken,
    JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteInPlaceAsync(source, selectToken,
      new(JsonPathEditKind.Remove), options, cancellationToken);

  /// <summary>Stages a streaming field projection, then overwrites the same seekable stream. See EditJsonInPlaceAsync.</summary>
  public static Task<long> ProjectJsonInPlaceAsync(this Stream source, string projectionPath,
    JsonPathEditingOptions options = default, CancellationToken cancellationToken = default)
    => JsonPathStreamEditor.RewriteInPlaceAsync(source, projectionPath,
      new(JsonPathEditKind.Project), options, cancellationToken);
}
