namespace JsonPathPlus;

/// <summary>Options for streaming JSON editing and projection write-back.</summary>
public readonly record struct JsonPathEditingOptions
{
  /// <summary>Depth, token-size and replay limits used while reading the source.</summary>
  public JsonPathExtractionOptions ReadOptions { get; init; }

  /// <summary>
  /// Maximum encoded size of a value passed to a JsonNode callback.
  /// Null means unlimited. Set, remove and projection do not materialize source values.
  /// </summary>
  public long? MaxMaterializedValueBytes { get; init; }
}
