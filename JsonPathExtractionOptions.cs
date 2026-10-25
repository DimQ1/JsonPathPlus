using System;

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

  /// <summary>Maximum JSON nesting depth. Zero uses the System.Text.Json default of 64.</summary>
  public int MaxDepth { get; init; }

  /// <summary>Maximum encoded size of an individual token, including its delimiters. Null means unlimited.</summary>
  public long? MaxTokenBytes { get; init; }

  /// <summary>Maximum size of a value captured for replay or materialization. Null means unlimited.</summary>
  public long? MaxCapturedValueBytes { get; init; }

  internal void Validate()
  {
    ArgumentOutOfRangeException.ThrowIfNegative(MaxDepth, nameof(MaxDepth));
    if (MaxTokenBytes is <= 0)
      throw new ArgumentOutOfRangeException(nameof(MaxTokenBytes));
    if (MaxCapturedValueBytes is <= 0)
      throw new ArgumentOutOfRangeException(nameof(MaxCapturedValueBytes));
  }
}
