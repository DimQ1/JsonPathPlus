using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

/// <summary>
/// Generates a JSON Schema (draft-07 compatible) from a <see cref="JsonNode"/> by inspecting its structure.
/// </summary>
internal static class JsonPathSchemaGenerator
{
  /// <summary>
  /// Generates a JSON Schema object for the given <paramref name="node"/>.
  /// Returns <c>null</c> when <paramref name="node"/> is <c>null</c>.
  /// </summary>
  public static JsonNode? GenerateSchema(JsonNode? node)
  {
    if (node is null)
      return CreateSchema("null");

    return node switch
    {
      JsonObject obj => GenerateObjectSchema(obj),
      JsonArray array => GenerateArraySchema(array),
      JsonValue value => GenerateValueSchema(value),
      _ => CreateSchema("null")
    };
  }

  /// <summary>
  /// Merges multiple data nodes into a single schema. Each data node is first converted
  /// to its individual schema, then all schemas are merged.
  /// When all schemas agree on a type, the common type is used.
  /// Mixing different types produces a <c>oneOf</c> union. Object schemas merge properties
  /// incrementally.
  /// </summary>
  internal static JsonNode? MergeSchemas(IReadOnlyList<JsonNode?> dataNodes)
  {
    var schemaNodes = new List<JsonNode>(dataNodes.Count);
    foreach (var node in dataNodes)
    {
      var schema = GenerateSchema(node);
      if (schema is not null)
        schemaNodes.Add(schema);
    }

    return MergeSchemaNodes(schemaNodes);
  }

  /// <summary>
  /// Merges already-generated schema nodes (no further <see cref="GenerateSchema"/> calls).
  /// </summary>
  private static JsonNode? MergeSchemaNodes(IReadOnlyList<JsonNode> schemas)
  {
    var nonNull = new List<JsonNode>(schemas.Count);
    foreach (var s in schemas)
    {
      if (s is not null)
        nonNull.Add(s);
    }

    if (nonNull.Count == 0)
      return CreateSchema("null");

    if (nonNull.Count == 1)
      return nonNull[0].DeepClone();

    // Group by type
    var types = new Dictionary<string, List<JsonNode>>(StringComparer.OrdinalIgnoreCase);

    foreach (var schema in nonNull)
    {
      if (schema is JsonObject obj && obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeName))
      {
        if (!types.ContainsKey(typeName))
          types[typeName] = new List<JsonNode>();
        types[typeName].Add(schema);
      }
    }

    // If there's only one type group, merge within that group
    if (types.Count == 1)
    {
      var (typeName, group) = types.First();
      return typeName switch
      {
        "object" => MergeObjectSchemas(group),
        "array" => MergeArraySchemas(group),
        _ => MergePrimitiveSchemas(typeName, group)
      };
    }

    // Multiple types → oneOf (deduplicated)
    var distinctSchemas = new List<JsonNode>();
    var seen = new HashSet<string>();

    foreach (var s in nonNull)
    {
      var json = s.ToJsonString();
      if (seen.Add(json))
        distinctSchemas.Add(s.DeepClone());
    }

    if (distinctSchemas.Count == 1)
      return distinctSchemas[0];

    var oneOf = new JsonObject();
    var oneOfArray = new JsonArray();
    foreach (var s in distinctSchemas)
      oneOfArray.Add(s);
    oneOf["oneOf"] = oneOfArray;
    return oneOf;
  }

  private static JsonObject GenerateObjectSchema(JsonObject obj)
  {
    var schema = new JsonObject
    {
      ["type"] = "object"
    };

    if (obj.Count > 0)
    {
      var properties = new JsonObject();
      var required = new JsonArray();

      foreach (var kvp in obj)
      {
        properties[kvp.Key] = GenerateSchema(kvp.Value);
        required.Add(kvp.Key);
      }

      schema["properties"] = properties;
      schema["required"] = required;
    }

    return schema;
  }

  private static JsonObject GenerateArraySchema(JsonArray array)
  {
    var schema = new JsonObject
    {
      ["type"] = "array"
    };

    if (array.Count > 0)
    {
      var itemSchemas = new List<JsonNode?>(array.Count);
      foreach (var item in array)
        itemSchemas.Add(GenerateSchema(item));

      var merged = MergeSchemas(itemSchemas!);
      if (merged is not null)
        schema["items"] = merged;
    }

    return schema;
  }

  private static JsonObject GenerateValueSchema(JsonValue value)
  {
    var kind = value.GetValueKind();
    return kind switch
    {
      System.Text.Json.JsonValueKind.String => CreateSchema("string"),
      System.Text.Json.JsonValueKind.Number => CreateSchema("number"),
      System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => CreateSchema("boolean"),
      System.Text.Json.JsonValueKind.Null => CreateSchema("null"),
      _ => CreateSchema("null")
    };
  }

  private static JsonObject CreateSchema(string type)
  {
    var schema = new JsonObject
    {
      ["type"] = type
    };
    return schema;
  }

  private static JsonObject MergeObjectSchemas(List<JsonNode> schemas)
  {
    var merged = new JsonObject
    {
      ["type"] = "object"
    };

    var allProperties = new Dictionary<string, List<JsonNode?>>(StringComparer.Ordinal);
    var propertyPresence = new Dictionary<string, int>(StringComparer.Ordinal);
    var totalCount = schemas.Count;

    foreach (var schema in schemas)
    {
      if (schema is not JsonObject obj || !obj.TryGetPropertyValue("properties", out var propsNode) || propsNode is not JsonObject properties)
        continue;

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

        mergedProperties[kvp.Key] = MergeSchemaNodes(propertySchemas);

        // Property is required if present in all schemas
        if (propertyPresence.TryGetValue(kvp.Key, out var count) && count == totalCount)
          required.Add(kvp.Key);
      }

      merged["properties"] = mergedProperties;
      if (required.Count > 0)
        merged["required"] = required;
    }

    return merged;
  }

  private static JsonObject MergeArraySchemas(List<JsonNode> schemas)
  {
    var merged = new JsonObject
    {
      ["type"] = "array"
    };

    var itemSchemas = new List<JsonNode>();
    foreach (var schema in schemas)
    {
      if (schema is JsonObject obj && obj.TryGetPropertyValue("items", out var items) && items is not null)
        itemSchemas.Add(items);
    }

    if (itemSchemas.Count > 0)
    {
      var mergedItems = MergeSchemaNodes(itemSchemas);
      if (mergedItems is not null)
        merged["items"] = mergedItems;
    }

    return merged;
  }

  private static JsonObject MergePrimitiveSchemas(string typeName, List<JsonNode> schemas)
  {
    // For same-type primitives, just return the canonical schema
    return CreateSchema(typeName);
  }
}
