using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JsonPathPlus;

/// <summary>
/// Generates a JSON Schema from <see cref="JsonNode"/> instances by inspecting their structure.
/// Supports draft-07 and 2020-12 dialects with configurable constraint inference.
/// </summary>
internal static class JsonPathSchemaGenerator
{
  // ── Public API (backward-compatible) ──────────────────────────────────

  /// <summary>
  /// Generates a JSON Schema for <paramref name="node"/> using full-inference 2020-12 options.
  /// Returns <c>null</c> when <paramref name="node"/> is <c>null</c>.
  /// </summary>
  public static JsonNode? GenerateSchema(JsonNode? node)
    => GenerateSchema(node, JsonPathSchemaGenerationOptions.FullInference);

  /// <summary>
  /// Generates a JSON Schema for <paramref name="node"/> with the given options.
  /// </summary>
  internal static JsonNode? GenerateSchema(JsonNode? node, JsonPathSchemaGenerationOptions options)
  {
    if (node is null)
      return CreateSchema("null", options);

    var ctx = new GenerationContext(options);
    return node switch
    {
      JsonObject obj => GenerateObjectSchema(obj, ctx),
      JsonArray array => GenerateArraySchema(array, ctx),
      JsonValue value => GenerateValueSchema(value, ctx),
      _ => CreateSchema("null", options)
    };
  }

  /// <summary>
  /// Merges multiple data nodes into a single schema.
  /// </summary>
  internal static JsonNode? MergeSchemas(IReadOnlyList<JsonNode?> dataNodes)
    => MergeSchemas(dataNodes, JsonPathSchemaGenerationOptions.FullInference);

  /// <summary>
  /// Merges multiple data nodes into a single schema with the given options.
  /// </summary>
  internal static JsonNode? MergeSchemas(IReadOnlyList<JsonNode?> dataNodes, JsonPathSchemaGenerationOptions options)
  {
    var ctx = new GenerationContext(options);
    var schemaNodes = new List<JsonNode>(dataNodes.Count);
    foreach (var node in dataNodes)
    {
      var schema = GenerateSchema(node, options);
      if (schema is not null)
        schemaNodes.Add(schema);
    }
    return MergeSchemaNodes(schemaNodes, ctx);
  }

  // ── Context ───────────────────────────────────────────────────────────

  private sealed class GenerationContext
  {
    public readonly JsonPathSchemaGenerationOptions Options;
    public readonly bool Is202012;

    public GenerationContext(JsonPathSchemaGenerationOptions options)
    {
      Options = options;
      Is202012 = options.SchemaDialect == JsonSchemaDialect.Draft202012;
    }
  }

  // ── Schema merging ────────────────────────────────────────────────────

  private static JsonNode? MergeSchemaNodes(IReadOnlyList<JsonNode> schemas, GenerationContext ctx)
  {
    var nonNull = new List<JsonNode>(schemas.Count);
    foreach (var s in schemas)
    {
      if (s is not null)
        nonNull.Add(s);
    }

    if (nonNull.Count == 0)
      return CreateSchema("null", ctx.Options);

    if (nonNull.Count == 1)
      return nonNull[0].DeepClone();

    // Group by type (supporting both string and array type)
    var groups = new Dictionary<string, List<JsonNode>>(StringComparer.OrdinalIgnoreCase);

    foreach (var schema in nonNull)
    {
      if (schema is not JsonObject obj || !obj.TryGetPropertyValue("type", out var tn))
        continue;

      var typeNames = GetTypeNames(tn);
      foreach (var typeName in typeNames)
      {
        if (!groups.ContainsKey(typeName))
          groups[typeName] = new List<JsonNode>();
        groups[typeName].Add(schema);
      }
    }

    // No typable schemas → collect distinct
    if (groups.Count == 0)
      return MakeOneOfOrAnyOf(DistinctSchemas(nonNull), ctx);

    // Single type group → merge within
    if (groups.Count == 1)
    {
      var (typeName, group) = groups.First();
      return typeName switch
      {
        "object" => MergeObjectSchemas(group, ctx),
        "array" => MergeArraySchemas(group, ctx),
        _ => MergeTypedSchemas(typeName, group, ctx)
      };
    }

    // Multiple type groups → oneOf (2020-12) or anyOf (draft-07)
    var mergedPerType = new List<JsonNode>();
    foreach (var (typeName, group) in groups)
    {
      var merged = typeName switch
      {
        "object" => MergeObjectSchemas(group, ctx),
        "array" => MergeArraySchemas(group, ctx),
        _ => MergeTypedSchemas(typeName, group, ctx)
      };
      mergedPerType.Add(merged);
    }

    // Also include schemas that had no type (if any)
    foreach (var s in nonNull)
    {
      if (s is JsonObject obj && obj.TryGetPropertyValue("type", out var tn) && tn is not null)
        continue;
      mergedPerType.Add(s.DeepClone());
    }

    return MakeOneOfOrAnyOf(DistinctSchemas(mergedPerType), ctx);
  }

  private static List<JsonNode> DistinctSchemas(List<JsonNode> schemas)
  {
    var distinct = new List<JsonNode>();
    var seen = new HashSet<string>();
    foreach (var s in schemas)
    {
      var json = s.ToJsonString();
      if (seen.Add(json))
        distinct.Add(s.DeepClone());
    }
    return distinct;
  }

  private static JsonNode MakeOneOfOrAnyOf(List<JsonNode> schemas, GenerationContext ctx)
  {
    if (schemas.Count == 1)
      return schemas[0];

    var wrapper = new JsonObject();
    var array = new JsonArray();
    foreach (var s in schemas)
      array.Add(s);

    wrapper[ctx.Is202012 ? "oneOf" : "anyOf"] = array;
    return wrapper;
  }

  // ── Type extraction ───────────────────────────────────────────────────

  private static string[] GetTypeNames(JsonNode? typeNode)
  {
    if (typeNode is JsonValue tv && tv.TryGetValue<string>(out var s))
      return [s];

    if (typeNode is JsonArray ta)
    {
      var names = new List<string>(ta.Count);
      foreach (var item in ta)
      {
        if (item is JsonValue iv && iv.TryGetValue<string>(out var name))
          names.Add(name);
      }
      return names.ToArray();
    }

    return [];
  }

  // ── Object schema ─────────────────────────────────────────────────────

  private static JsonObject GenerateObjectSchema(JsonObject obj, GenerationContext ctx)
  {
    var schema = CreateSchemaBase("object", ctx);

    if (obj.Count == 0)
      return schema;

    var properties = new JsonObject();
    var required = new JsonArray();

    foreach (var kvp in obj)
    {
      properties[kvp.Key] = GenerateSchema(kvp.Value, ctx.Options);
      required.Add(kvp.Key);
    }

    schema["properties"] = properties;
    schema["required"] = required;

    if (ctx.Options.InferConstraints)
    {
      schema["minProperties"] = obj.Count;
      schema["maxProperties"] = obj.Count;
    }

    if (ctx.Options.StrictAdditionalProperties)
      schema["additionalProperties"] = false;

    if (ctx.Options.StrictUnevaluatedProperties && ctx.Is202012)
      schema["unevaluatedProperties"] = false;

    return schema;
  }

  // ── Array schema ──────────────────────────────────────────────────────

  private static JsonObject GenerateArraySchema(JsonArray array, GenerationContext ctx)
  {
    var schema = CreateSchemaBase("array", ctx);

    if (array.Count == 0)
      return schema;

    // Build item schemas
    var itemSchemas = new List<JsonNode?>(array.Count);
    foreach (var item in array)
      itemSchemas.Add(GenerateSchema(item, ctx.Options));

    var merged = MergeSchemas(itemSchemas!, ctx.Options);
    if (merged is not null)
      schema["items"] = merged;

    if (ctx.Options.InferConstraints)
    {
      schema["minItems"] = array.Count;
      schema["maxItems"] = array.Count;
    }

    if (ctx.Options.InferUniqueItems && array.Count > 1 && AllUnique(array))
      schema["uniqueItems"] = true;

    return schema;
  }

  private static bool AllUnique(JsonArray array)
  {
    var seen = new HashSet<string>(array.Count);
    foreach (var item in array)
    {
      var json = item?.ToJsonString() ?? "null";
      if (!seen.Add(json))
        return false;
    }
    return true;
  }

  // ── Value schema ──────────────────────────────────────────────────────

  private static JsonObject GenerateValueSchema(JsonValue value, GenerationContext ctx)
  {
    var kind = value.GetValueKind();

    switch (kind)
    {
      case JsonValueKind.String:
        return GenerateStringSchema(value, ctx);

      case JsonValueKind.Number:
        return GenerateNumberSchema(value, ctx);

      case JsonValueKind.True:
      case JsonValueKind.False:
      {
        var bSchema = CreateSchemaBase("boolean", ctx);
        if (ctx.Options.InferConst)
          bSchema["const"] = value.GetValue<bool>();
        return bSchema;
      }

      case JsonValueKind.Null:
        return CreateSchema("null", ctx.Options);

      default:
        return CreateSchema("null", ctx.Options);
    }
  }

  private static JsonObject GenerateStringSchema(JsonValue value, GenerationContext ctx)
  {
    var schema = CreateSchemaBase("string", ctx);

    if (value.TryGetValue<string>(out var str) && str is not null)
    {
      if (ctx.Options.InferConstraints)
      {
        schema["minLength"] = str.Length;
        schema["maxLength"] = str.Length;
      }

      if (ctx.Options.InferConst)
        schema["const"] = str;

      if (ctx.Options.InferFormat)
      {
        var format = DetectFormat(str, ctx);
        if (format is not null)
          schema["format"] = format;
      }

      if (ctx.Options.InferPattern)
      {
        var pattern = DetectPattern(str);
        if (pattern is not null)
          schema["pattern"] = pattern;
      }
    }

    return schema;
  }

  private static JsonObject GenerateNumberSchema(JsonValue value, GenerationContext ctx)
  {
    var isInt = false;
    double? numVal = null;
    decimal? decVal = null;

    if (value.TryGetValue<decimal>(out var dec))
    {
      decVal = dec;
      isInt = dec == Math.Truncate(dec);
      numVal = (double)dec;
    }
    else if (value.TryGetValue<double>(out var dbl))
    {
      numVal = dbl;
      isInt = dbl == Math.Truncate(dbl);
    }
    else if (value.TryGetValue<long>(out var lng))
    {
      numVal = lng;
      isInt = true;
    }

    var typeName = isInt && ctx.Is202012 ? "integer" : "number";
    var schema = CreateSchemaBase(typeName, ctx);

    if (ctx.Options.InferConstraints && numVal.HasValue)
    {
      schema["minimum"] = numVal.Value;
      schema["maximum"] = numVal.Value;
    }

    if (ctx.Options.InferConst && numVal.HasValue)
      schema["const"] = decVal.HasValue ? JsonValue.Create(decVal.Value) : JsonValue.Create(numVal.Value);

    return schema;
  }

  // ── Format detection ──────────────────────────────────────────────────

  private static string? DetectFormat(string value, GenerationContext ctx)
  {
    if (string.IsNullOrEmpty(value))
      return null;

    // uuid
    if (UuidRegex.IsMatch(value))
      return "uuid";

    // date-time: ISO 8601 with T separator and time
    if (value.Contains('T') && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
      return "date-time";

    // date: YYYY-MM-DD
    if (DateRegex.IsMatch(value) && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
      return "date";

    // time: HH:MM:SS with optional ms and tz
    if (TimeRegex.IsMatch(value))
      return "time";

    // ipv4
    if (Ipv4Regex.IsMatch(value))
      return "ipv4";

    // ipv6
    if (Ipv6Regex.IsMatch(value))
      return "ipv6";

    // email
    if (EmailRegex.IsMatch(value))
      return "email";

    // uri
    if (UriRegex.IsMatch(value))
      return "uri";

    // json-pointer (low confidence unless HighConfidenceFormatOnly is false)
    if (value.StartsWith('/') && !ctx.Options.HighConfidenceFormatOnly)
      return "json-pointer";

    // uri-reference (low confidence)
    if (UriReferenceRegex.IsMatch(value) && !ctx.Options.HighConfidenceFormatOnly && !value.Contains(' '))
      return "uri-reference";

    // hostname (medium- requires dots, no spaces)
    if (HostnameRegex.IsMatch(value) && !ctx.Options.HighConfidenceFormatOnly)
      return "hostname";

    return null;
  }

  // ── Pattern detection ─────────────────────────────────────────────────

  private static string? DetectPattern(string value)
  {
    if (string.IsNullOrEmpty(value) || value.Length < 4)
      return null;

    if (AllDigitsRegex.IsMatch(value))
      return @"^\d+$";

    if (AllLettersRegex.IsMatch(value))
      return @"^[a-zA-Z]+$";

    if (AllAlphaNumericRegex.IsMatch(value) && AnyLetterRegex.IsMatch(value) && AnyDigitRegex.IsMatch(value))
      return @"^[a-zA-Z0-9]+$";

    return null;
  }

  // ── Object schema merging ─────────────────────────────────────────────

  private static JsonObject MergeObjectSchemas(List<JsonNode> schemas, GenerationContext ctx)
  {
    var merged = CreateSchemaBase("object", ctx);
    var totalCount = schemas.Count;

    var allProperties = new Dictionary<string, List<JsonNode?>>(StringComparer.Ordinal);
    var propertyPresence = new Dictionary<string, int>(StringComparer.Ordinal);
    int minProps = int.MaxValue;
    int maxProps = int.MinValue;

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj || !obj.TryGetPropertyValue("properties", out var propsNode) || propsNode is not JsonObject properties)
        continue;

      var propCount = properties.Count;
      if (propCount < minProps) minProps = propCount;
      if (propCount > maxProps) maxProps = propCount;

      foreach (var kvp in properties)
      {
        if (!allProperties.ContainsKey(kvp.Key))
          allProperties[kvp.Key] = new List<JsonNode?>();
        allProperties[kvp.Key].Add(kvp.Value);

        if (!propertyPresence.ContainsKey(kvp.Key))
          propertyPresence[kvp.Key] = 0;
        propertyPresence[kvp.Key]++;
      }
    }

    if (allProperties.Count > 0)
    {
      var mergedProperties = new JsonObject();
      var required = new JsonArray();

      foreach (var kvp in allProperties)
      {
        var propertySchemas = new List<JsonNode>();
        foreach (var ps in kvp.Value)
        {
          if (ps is not null)
            propertySchemas.Add(ps);
        }

        mergedProperties[kvp.Key] = MergeSchemaNodes(propertySchemas, ctx);

        // Property is required if present in all schemas
        if (propertyPresence.TryGetValue(kvp.Key, out var count) && count == totalCount)
          required.Add(kvp.Key);
      }

      merged["properties"] = mergedProperties;
      if (required.Count > 0)
        merged["required"] = required;
    }

    if (ctx.Options.InferConstraints)
    {
      if (minProps != int.MaxValue) merged["minProperties"] = minProps;
      if (maxProps != int.MinValue) merged["maxProperties"] = maxProps;
    }

    if (ctx.Options.StrictAdditionalProperties)
      merged["additionalProperties"] = false;

    if (ctx.Options.StrictUnevaluatedProperties && ctx.Is202012)
      merged["unevaluatedProperties"] = false;

    // dependentRequired inference
    if (ctx.Options.InferDependentRequired && allProperties.Count > 0)
    {
      var depReq = InferDependentRequired(schemas, totalCount);
      if (depReq is not null && depReq.Count > 0)
        merged["dependentRequired"] = depReq;
    }

    return merged;
  }

  private static JsonObject? InferDependentRequired(List<JsonNode> schemas, int totalSchemas)
  {
    // Track: for each property key, how often does it co-occur with others?
    var cooccurrence = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
    var presenceCount = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj || !obj.TryGetPropertyValue("properties", out var propsNode) || propsNode is not JsonObject properties)
        continue;

      var presentKeys = new HashSet<string>(StringComparer.Ordinal);
      foreach (var pk in properties)
        presentKeys.Add(pk.Key);

      foreach (var key in presentKeys)
      {
        if (!presenceCount.ContainsKey(key))
          presenceCount[key] = 0;
        presenceCount[key]++;

        if (!cooccurrence.ContainsKey(key))
          cooccurrence[key] = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var other in presentKeys)
        {
          if (other == key) continue;
          if (!cooccurrence[key].ContainsKey(other))
            cooccurrence[key][other] = 0;
          cooccurrence[key][other]++;
        }
      }
    }

    // If key X appears N times and Y appears alongside X N times, Y depends on X
    var result = new JsonObject();
    foreach (var (key, correlations) in cooccurrence)
    {
      if (!presenceCount.TryGetValue(key, out var keyTotal) || keyTotal < 2)
        continue;

      var deps = new JsonArray();
      foreach (var (other, coCount) in correlations)
      {
        // Only include if co-occurrence is 100% and independent appearances differ
        if (coCount == keyTotal && coCount >= 2 && totalSchemas >= 2)
          deps.Add(other);
      }

      if (deps.Count > 0)
        result[key] = deps;
    }

    return result.Count > 0 ? result : null;
  }

  // ── Array schema merging ──────────────────────────────────────────────

  private static JsonObject MergeArraySchemas(List<JsonNode> schemas, GenerationContext ctx)
  {
    var merged = CreateSchemaBase("array", ctx);

    var itemSchemas = new List<JsonNode>();
    var prefixItemLists = new List<JsonArray>();
    int minItems = int.MaxValue;
    int maxItems = int.MinValue;
    bool allUnique = true;

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj)
        continue;

      // Collect items
      if (obj.TryGetPropertyValue("items", out var items) && items is not null)
        itemSchemas.Add(items);

      // Collect prefixItems
      if (obj.TryGetPropertyValue("prefixItems", out var prefix) && prefix is JsonArray pa)
        prefixItemLists.Add(pa);

      // Collect min/max
      if (obj.TryGetPropertyValue("minItems", out var minNode) && minNode is JsonValue minV && minV.TryGetValue<int>(out var min))
        minItems = Math.Min(minItems, min);

      if (obj.TryGetPropertyValue("maxItems", out var maxNode) && maxNode is JsonValue maxV && maxV.TryGetValue<int>(out var max))
        maxItems = Math.Max(maxItems, max);

      // Track uniqueness
      if (obj.TryGetPropertyValue("uniqueItems", out var uiNode) && uiNode is JsonValue uiV && uiV.GetValueKind() == JsonValueKind.False)
        allUnique = false;
    }

    if (itemSchemas.Count > 0)
    {
      var mergedItems = MergeSchemaNodes(itemSchemas, ctx);
      if (mergedItems is not null)
        merged["items"] = mergedItems;
    }

    if (minItems != int.MaxValue)
      merged["minItems"] = minItems;
    if (maxItems != int.MinValue)
      merged["maxItems"] = maxItems;

    if (ctx.Options.InferUniqueItems && allUnique)
      merged["uniqueItems"] = true;

    if (ctx.Options.StrictUnevaluatedItems && ctx.Is202012)
      merged["unevaluatedItems"] = false;

    return merged;
  }

  // ── Typed schema merging (primitives) ─────────────────────────────────

  private static JsonObject MergeTypedSchemas(string typeName, List<JsonNode> schemas, GenerationContext ctx)
  {
    var merged = CreateSchemaBase(typeName, ctx);

    if (!ctx.Options.InferConstraints)
      return merged;

    if (typeName is "number" or "integer")
      MergeNumericConstraints(merged, schemas, ctx);
    else if (typeName is "string")
      MergeStringConstraints(merged, schemas, ctx, typeName);
    else if (typeName is "boolean")
      MergeBooleanConstraints(merged, schemas, ctx);

    return merged;
  }

  private static void MergeNumericConstraints(JsonObject merged, List<JsonNode> schemas, GenerationContext ctx)
  {
    double? gMin = null, gMax = null;
    var consts = new List<(JsonNode Node, double Value)>();

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj) continue;

      if (obj.TryGetPropertyValue("minimum", out var minNode) && minNode is JsonValue minV && minV.TryGetValue<double>(out var min))
        gMin = gMin.HasValue ? Math.Min(gMin.Value, min) : min;

      if (obj.TryGetPropertyValue("maximum", out var maxNode) && maxNode is JsonValue maxV && maxV.TryGetValue<double>(out var max))
        gMax = gMax.HasValue ? Math.Max(gMax.Value, max) : max;

      if (obj.TryGetPropertyValue("const", out var cn) && cn is JsonValue cv && cv.TryGetValue<double>(out var cd))
        consts.Add((cv, cd));
    }

    if (gMin.HasValue) merged["minimum"] = gMin.Value;
    if (gMax.HasValue) merged["maximum"] = gMax.Value;

    // Enum inference
    if (ctx.Options.InferEnum && consts.Count >= 2 && consts.Count <= ctx.Options.MaxEnumValues)
    {
      var distinct = new HashSet<double>();
      var enumArr = new JsonArray();
      foreach (var (node, val) in consts)
      {
        if (distinct.Add(val))
          enumArr.Add(node.DeepClone());
      }
      if (enumArr.Count >= 2)
        merged["enum"] = enumArr;
    }
    else if (ctx.Options.InferConst && consts.Count == schemas.Count && consts.Count >= 1)
    {
      // All have same const? Check
      bool same = true;
      for (int i = 1; i < consts.Count; i++)
      {
        if (consts[i].Value != consts[0].Value) { same = false; break; }
      }
      if (same) merged["const"] = consts[0].Value;
    }
  }

  private static void MergeStringConstraints(JsonObject merged, List<JsonNode> schemas, GenerationContext ctx, string typeName)
  {
    int? gMinLen = null, gMaxLen = null;
    string? gConst = null;
    bool allSameConst = true;
    var gFormats = new HashSet<string>(StringComparer.Ordinal);
    var gPatterns = new HashSet<string>(StringComparer.Ordinal);
    var allConsts = new List<string>();

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj) continue;

      if (obj.TryGetPropertyValue("minLength", out var minL) && minL is JsonValue ml && ml.TryGetValue<int>(out var minLenVal))
        gMinLen = gMinLen.HasValue ? Math.Min(gMinLen.Value, minLenVal) : minLenVal;

      if (obj.TryGetPropertyValue("maxLength", out var maxL) && maxL is JsonValue xl && xl.TryGetValue<int>(out var maxLenVal))
        gMaxLen = gMaxLen.HasValue ? Math.Max(gMaxLen.Value, maxLenVal) : maxLenVal;

      if (obj.TryGetPropertyValue("const", out var cn) && cn is not null)
      {
        var cs = cn.ToString();
        allConsts.Add(cs);
        if (gConst is null) gConst = cs;
        else if (gConst != cs) allSameConst = false;
      }
      else
      {
        allSameConst = false;
      }

      if (obj.TryGetPropertyValue("format", out var fn) && fn is JsonValue fv && fv.TryGetValue<string>(out var fmt) && fmt is not null)
        gFormats.Add(fmt);

      if (obj.TryGetPropertyValue("pattern", out var pn) && pn is JsonValue pv && pv.TryGetValue<string>(out var pat) && pat is not null)
        gPatterns.Add(pat);
    }

    if (gMinLen.HasValue) merged["minLength"] = gMinLen.Value;
    if (gMaxLen.HasValue) merged["maxLength"] = gMaxLen.Value;

    // Format: keep only if all agree on the same format
    if (gFormats.Count == 1)
      merged["format"] = gFormats.First();

    // Pattern: keep only if all agree
    if (gPatterns.Count == 1)
      merged["pattern"] = gPatterns.First();

    // Enum inference for strings
    var distinctConsts = new HashSet<string>(allConsts);
    if (ctx.Options.InferEnum && distinctConsts.Count >= 2 && distinctConsts.Count <= ctx.Options.MaxEnumValues && allConsts.Count == schemas.Count)
    {
      var enumArr = new JsonArray();
      foreach (var c in distinctConsts)
        enumArr.Add(c);
      merged["enum"] = enumArr;
    }
    else if (ctx.Options.InferConst && allSameConst && gConst is not null && allConsts.Count == schemas.Count)
    {
      merged["const"] = gConst;
    }
  }

  private static void MergeBooleanConstraints(JsonObject merged, List<JsonNode> schemas, GenerationContext ctx)
  {
    // If both true and false observed, remove any const
    bool hasTrue = false, hasFalse = false;
    foreach (var schema in schemas)
    {
      if (schema is JsonObject obj && obj.TryGetPropertyValue("const", out var cn) && cn is JsonValue cv)
      {
        if (cv.GetValueKind() == JsonValueKind.True) hasTrue = true;
        if (cv.GetValueKind() == JsonValueKind.False) hasFalse = true;
      }
    }
    if (hasTrue && hasFalse && schemas.Count == 2)
    {
      merged.Remove("const");
    }
  }

  // ── Helpers ───────────────────────────────────────────────────────────

  private static JsonObject CreateSchema(string type, JsonPathSchemaGenerationOptions options)
  {
    var schema = new JsonObject { ["type"] = type };
    ApplyDialectAndMeta(schema, options);
    return schema;
  }

  private static JsonObject CreateSchemaBase(string type, GenerationContext ctx)
  {
    var schema = new JsonObject { ["type"] = type };
    ApplyDialectAndMeta(schema, ctx.Options);
    return schema;
  }

  private static void ApplyDialectAndMeta(JsonObject schema, JsonPathSchemaGenerationOptions options)
  {
    if (options.SchemaDialect == JsonSchemaDialect.Draft202012)
      schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
    else
      schema["$schema"] = "http://json-schema.org/draft-07/schema#";

    if (options.SchemaId is not null)
      schema["$id"] = options.SchemaId;

    if (options.Comment is not null)
      schema["$comment"] = options.Comment;
  }

  // ── Cached regexes ────────────────────────────────────────────────────

  private static readonly Regex UuidRegex = new(
    @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex DateRegex = new(
    @"^\d{4}-\d{2}-\d{2}$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex TimeRegex = new(
    @"^\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex Ipv4Regex = new(
    @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex Ipv6Regex = new(
    @"^([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}$|^::$|^([0-9a-fA-F]{1,4}:)*:([0-9a-fA-F]{1,4}:)*[0-9a-fA-F]{1,4}$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex EmailRegex = new(
    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex UriRegex = new(
    @"^[a-zA-Z][a-zA-Z0-9+.-]*://\S+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex UriReferenceRegex = new(
    @"^([a-zA-Z][a-zA-Z0-9+.-]*://\S+)|(/\S*)|(\.\.?/\S*)|(#\S*)$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex HostnameRegex = new(
    @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex AllDigitsRegex = new(
    @"^\d+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex AllLettersRegex = new(
    @"^[a-zA-Z]+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex AllAlphaNumericRegex = new(
    @"^[a-zA-Z0-9]+$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex AnyLetterRegex = new(
    @"[a-zA-Z]",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly Regex AnyDigitRegex = new(
    @"\d",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

