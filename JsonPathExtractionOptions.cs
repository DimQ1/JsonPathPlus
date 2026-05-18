namespace JsonPathPlus;

/// <summary>
/// Options that influence stream-based extraction behavior.
/// </summary>
public readonly record struct JsonPathExtractionOptions
{
  /// <summary>
  /// Maximum number of bytes allowed for non-streaming full-document parsing.
  /// When <c>null</c>, no size cap is enforced.
  /// </summary>
  public long? FullParseMaxBytes { get; init; }
}
