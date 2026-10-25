using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal sealed class JsonPathFilterDependencies
{
  private Dictionary<string, JsonPathFilterDependencies>? _children;
  private bool _requiresValue;

  public sealed class Observer
  {
    private readonly List<(int Depth, JsonPathFilterDependencies Dependencies, JsonObject Value)> _frames = new();
    private JsonPathFilterDependencies? _pending;
    private JsonObject? _parent;
    private string? _name;
    public bool Supported { get; private set; } = true;
    public JsonNode? Result { get; }

    internal Observer(JsonPathFilterDependencies dependencies, JsonTokenType token, int depth)
    {
      Supported = !dependencies._requiresValue;
      if (token == JsonTokenType.StartObject)
      {
        var result = new JsonObject();
        Result = result;
        _frames.Add((depth, dependencies, result));
      }
      else
        Result = token == JsonTokenType.Null ? null : JsonValue.Create(true);
    }

    public void Read(ref Utf8JsonReader reader)
    {
      if (!Supported)
        return;
      if (reader.TokenType == JsonTokenType.PropertyName)
      {
        _pending = null;
        if (_frames.Count == 0)
          return;
        var frame = _frames[^1];
        if (frame.Depth + 1 != reader.CurrentDepth || frame.Dependencies._children is null)
          return;
        foreach (var property in frame.Dependencies._children)
        {
          if (reader.ValueTextEquals(property.Key))
          {
            _pending = property.Value;
            _name = property.Key;
            _parent = frame.Value;
            break;
          }
        }
        return;
      }
      if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
      {
        if (_frames.Count > 0 && _frames[^1].Depth == reader.CurrentDepth)
          _frames.RemoveAt(_frames.Count - 1);
        return;
      }
      if (_pending is null)
        return;
      var dependency = _pending;
      _pending = null;
      if (reader.TokenType == JsonTokenType.Null)
        _parent![_name!] = null;
      else if (dependency._requiresValue)
      {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
          Supported = false;
        else
        {
          using var scalar = JsonDocument.ParseValue(ref reader);
          _parent![_name!] = JsonPathElementMatcher.Materialize(scalar.RootElement);
        }
      }
      else if (reader.TokenType == JsonTokenType.StartObject && dependency._children is not null)
      {
        var child = new JsonObject();
        _parent![_name!] = child;
        _frames.Add((reader.CurrentDepth, dependency, child));
      }
      else
        _parent![_name!] = JsonValue.Create(true);
    }
  }

  public Observer Observe(JsonTokenType token, int depth) => new(this, token, depth);

  public void Add(string[] parts, bool presenceOnly)
  {
    var node = this;
    foreach (var part in parts)
    {
      node._children ??= new(StringComparer.Ordinal);
      if (!node._children.TryGetValue(part, out var child))
        node._children.Add(part, child = new());
      node = child;
    }
    node._requiresValue |= !presenceOnly;
  }

  public async ValueTask<JsonNode?> ReadAsync(JsonPathStreamReader reader)
  {
    if (_requiresValue)
    {
      using var value = await reader.ReadValueAsync().ConfigureAwait(false);
      return JsonPathElementMatcher.Materialize(value.RootElement);
    }
    if (_children is not null && reader.TokenType == JsonTokenType.StartObject)
    {
      var result = new JsonObject();
      while (await reader.ReadAsync().ConfigureAwait(false))
      {
        if (reader.TokenType == JsonTokenType.EndObject)
          return result;
        if (reader.TokenType != JsonTokenType.PropertyName)
          throw new JsonException("Expected a JSON property.");
        var name = reader.PropertyName!;
        if (!await reader.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing JSON property value.");
        if (_children.TryGetValue(name, out var child))
          result[name] = await child.ReadAsync(reader).ConfigureAwait(false);
        else
          await reader.SkipValueAsync().ConfigureAwait(false);
      }
      throw new JsonException("Incomplete JSON object.");
    }
    var isNull = reader.TokenType == JsonTokenType.Null;
    await reader.SkipValueAsync().ConfigureAwait(false);
    return isNull ? null : JsonValue.Create(true);
  }
}
