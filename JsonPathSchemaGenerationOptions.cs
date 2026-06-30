using System;

namespace JsonPathPlus;

/// <summary>
/// Controls the dialect and inference behavior of the JSON Schema generator.
/// </summary>
public readonly record struct JsonPathSchemaGenerationOptions
{
  /// <summary>
  /// The JSON Schema dialect to emit. Defaults to <see cref="JsonSchemaDialect.Draft202012"/>.
  /// </summary>
  public JsonSchemaDialect SchemaDialect { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator infers numeric constraints (minimum, maximum),
  /// string length constraints (minLength, maxLength), array size constraints
  /// (minItems, maxItems), and object size constraints (minProperties, maxProperties).
  /// Defaults to <c>true</c>.
  /// </summary>
  public bool InferConstraints { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator emits <c>enum</c> when the number of distinct
  /// values across samples is ≤ <see cref="MaxEnumValues"/>. Defaults to <c>true</c>.
  /// </summary>
  public bool InferEnum { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator emits <c>const</c> when all samples for a
  /// given value are identical. Defaults to <c>true</c>.
  /// </summary>
  public bool InferConst { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator attempts to auto-detect semantic formats
  /// (date-time, email, uri, uuid, ipv4, ipv6, hostname, json-pointer, regex).
  /// Defaults to <c>true</c>.
  /// </summary>
  public bool InferFormat { get; init; }

  /// <summary>
  /// When <c>true</c>, string values are analyzed for common patterns and a
  /// <c>pattern</c> keyword is emitted. Defaults to <c>true</c>.
  /// </summary>
  public bool InferPattern { get; init; }

  /// <summary>
  /// When <c>true</c>, arrays with all-unique elements get <c>uniqueItems: true</c>.
  /// Defaults to <c>true</c>.
  /// </summary>
  public bool InferUniqueItems { get; init; }

  /// <summary>
  /// Maximum number of distinct values to include in an <c>enum</c>.
  /// Defaults to <c>10</c>.
  /// </summary>
  public int MaxEnumValues { get; init; }

  /// <summary>
  /// When <c>true</c>, object schemas include <c>additionalProperties: false</c>
  /// when all observed properties are known. Defaults to <c>false</c>.
  /// </summary>
  public bool StrictAdditionalProperties { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator emits <c>unevaluatedProperties: false</c>
  /// for objects (2020-12 dialect only). Defaults to <c>false</c>.
  /// </summary>
  public bool StrictUnevaluatedProperties { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator emits <c>unevaluatedItems: false</c>
  /// for arrays (2020-12 dialect only). Defaults to <c>false</c>.
  /// </summary>
  public bool StrictUnevaluatedItems { get; init; }

  /// <summary>
  /// When <c>true</c>, meta-data annotations (title, description, default,
  /// examples) are generated. Defaults to <c>false</c>.
  /// </summary>
  public bool GenerateMetaData { get; init; }

  /// <summary>
  /// Optional <c>$id</c> URI for the generated schema.
  /// </summary>
  public string? SchemaId { get; init; }

  /// <summary>
  /// Optional <c>$comment</c> for the generated schema.
  /// </summary>
  public string? Comment { get; init; }

  /// <summary>
  /// When <c>true</c>, the generator uses <c>$defs</c> for reusable object schemas
  /// that appear in multiple places. Defaults to <c>false</c>.
  /// </summary>
  public bool UseDefs { get; init; }

  /// <summary>
  /// When <c>true</c>, <c>dependentRequired</c> is inferred from correlated
  /// property presence in merged schemas. Defaults to <c>true</c>.
  /// </summary>
  public bool InferDependentRequired { get; init; }

  /// <summary>
  /// When <c>true</c>, format detection must have high confidence.
  /// When <c>false</c>, medium confidence is sufficient.
  /// Defaults to <c>true</c>.
  /// </summary>
  public bool HighConfidenceFormatOnly { get; init; }

  /// <summary>
  /// Creates options with all inference enabled and 2020-12 dialect.
  /// </summary>
  public static readonly JsonPathSchemaGenerationOptions FullInference = new()
  {
    SchemaDialect = JsonSchemaDialect.Draft202012,
    InferConstraints = true,
    InferEnum = true,
    InferConst = true,
    InferFormat = true,
    InferPattern = true,
    InferUniqueItems = true,
    MaxEnumValues = 10,
    StrictAdditionalProperties = false,
    StrictUnevaluatedProperties = false,
    StrictUnevaluatedItems = false,
    GenerateMetaData = false,
    UseDefs = false,
    InferDependentRequired = true,
    HighConfidenceFormatOnly = true,
  };

  /// <summary>
  /// Creates options compatible with draft-07 behavior (no new inference).
  /// </summary>
  public static readonly JsonPathSchemaGenerationOptions Draft07Compatible = new()
  {
    SchemaDialect = JsonSchemaDialect.Draft07,
    InferConstraints = false,
    InferEnum = false,
    InferConst = false,
    InferFormat = false,
    InferPattern = false,
    InferUniqueItems = false,
    MaxEnumValues = 0,
    StrictAdditionalProperties = false,
    StrictUnevaluatedProperties = false,
    StrictUnevaluatedItems = false,
    GenerateMetaData = false,
    UseDefs = false,
    InferDependentRequired = false,
    HighConfidenceFormatOnly = true,
  };
}

/// <summary>
/// JSON Schema dialect version.
/// </summary>
public enum JsonSchemaDialect
{
  /// <summary>http://json-schema.org/draft-07/schema#</summary>
  Draft07,

  /// <summary>https://json-schema.org/draft/2020-12/schema</summary>
  Draft202012,
}
