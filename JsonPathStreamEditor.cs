using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal enum JsonPathEditKind { Callback, Set, Remove, Project }

internal readonly record struct JsonPathEditOperation(
  JsonPathEditKind Kind, Func<JsonNode?, JsonNode?>? Callback = null, JsonNode? Value = null,
  string[]? Fields = null, bool Exclude = false);

internal sealed class JsonPathStreamEditor
{
  private sealed class Frame
  {
    public readonly List<int> States = new();
    public readonly List<int> Filters = new();
    public ArrayBufferWriter<byte>? NodeBuffer;
    public Utf8JsonWriter? NodeWriter;
  }

  private static readonly byte[] Symbols = "{}[],:"u8.ToArray();
  private readonly List<JsonPathSegment> _segments;
  private readonly JsonPathEditOperation _operation;
  private readonly JsonPathEditingOptions _options;
  private readonly Stream _output;
  private readonly CancellationToken _cancellationToken;
  private readonly List<Frame> _frames = new();
  private readonly JsonPathFilterNode?[] _filters;
  private readonly JsonPathFilterDependencies?[] _dependencies;
  private readonly bool[] _canProject;
  private long _matches;
  private int MaxDepth => _options.ReadOptions.MaxDepth == 0 ? 64 : _options.ReadOptions.MaxDepth;

  private JsonPathStreamEditor(List<JsonPathSegment> segments, JsonPathEditOperation operation,
    JsonPathEditingOptions options, Stream output, CancellationToken cancellationToken)
  {
    _segments = segments;
    _operation = operation;
    _options = options;
    _output = output;
    _cancellationToken = cancellationToken;
    _filters = new JsonPathFilterNode?[segments.Count];
    _dependencies = new JsonPathFilterDependencies?[segments.Count];
    _canProject = new bool[segments.Count];
    for (var i = 0; i < segments.Count; i++)
    {
      if (segments[i].SegmentType != JsonPathSegmentType.Filter)
        continue;
      var filter = segments[i].FilterNode ?? JsonPathFilterNode.Compile(segments[i].FilterExpression ?? string.Empty);
      var dependencies = new JsonPathFilterDependencies();
      _filters[i] = filter;
      _dependencies[i] = dependencies;
      _canProject[i] = filter.CollectDependencies(dependencies);
    }
  }

  public static async Task<long> RewriteAsync(Stream source, Stream destination, string? path,
    JsonPathEditOperation operation, JsonPathEditingOptions options, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(destination);
    if (!source.CanRead)
      throw new ArgumentException("The source stream must be readable.", nameof(source));
    if (!destination.CanWrite)
      throw new ArgumentException("The destination stream must be writable.", nameof(destination));
    if (ReferenceEquals(source, destination) || SameFile(source, destination))
      throw new ArgumentException("Use an in-place editing overload when source and destination share storage.", nameof(destination));
    options.ReadOptions.Validate();
    if (options.MaxMaterializedValueBytes is <= 0)
      throw new ArgumentOutOfRangeException(nameof(options.MaxMaterializedValueBytes));
    cancellationToken.ThrowIfCancellationRequested();
    var segments = ParsePlan(path, ref operation);
    var origin = source.CanSeek ? source.Position : -1;
    using var output = new JsonPathBufferedWriteStream(destination);
    using var reader = new JsonPathStreamReader(source, cancellationToken, options.ReadOptions);
    var editor = new JsonPathStreamEditor(segments, operation, options, output, cancellationToken);
    try
    {
      if (!await reader.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing JSON document.");
      var root = editor.GetFrame(0);
      root.States.Add(0);
      await editor.WriteValueAsync(reader, root.States, 0, null, false).ConfigureAwait(false);
      if (await reader.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Unexpected content after the JSON document.");
      await output.FlushAsync(cancellationToken).ConfigureAwait(false);
      return editor._matches;
    }
    finally
    {
      foreach (var frame in editor._frames)
        frame.NodeWriter?.Dispose();
      if (origin >= 0)
        source.Position = origin;
    }
  }

  public static async Task<long> RewriteInPlaceAsync(Stream source, string? path,
    JsonPathEditOperation operation, JsonPathEditingOptions options, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(source);
    if (!source.CanSeek || !source.CanRead || !source.CanWrite)
      throw new ArgumentException("In-place editing requires a readable, writable, seekable stream.", nameof(source));
    cancellationToken.ThrowIfCancellationRequested();
    var origin = source.Position;
    using var staged = JsonPathValueBuffer.CreateTemporaryStream();
    var matches = await RewriteAsync(source, staged, path, operation, options, cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    staged.Position = 0;
    try
    {
      var length = checked(origin + staged.Length);
      if (source is MemoryStream memory && length > memory.Capacity)
        memory.Capacity = checked((int)length);
      source.SetLength(source.Length);
      source.Position = origin;
      await staged.CopyToAsync(source, 16384, CancellationToken.None).ConfigureAwait(false);
      source.SetLength(length);
      await source.FlushAsync(CancellationToken.None).ConfigureAwait(false);
      return matches;
    }
    finally
    {
      source.Position = origin;
    }
  }

  private static bool SameFile(Stream source, Stream destination)
    => source is FileStream a && destination is FileStream b && string.Equals(
      Path.GetFullPath(a.Name), Path.GetFullPath(b.Name),
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

  private static List<JsonPathSegment> ParsePlan(string? path, ref JsonPathEditOperation operation)
  {
    var validation = JsonPathValidator.Validate(path);
    if (!validation.IsValid)
      throw new ArgumentException(validation.Error, nameof(path));
    var segments = JsonPathParser.Parse(path.AsSpan(), strict: true);
    if (operation.Kind == JsonPathEditKind.Project)
    {
      if (segments.Count == 0 || segments[^1].SegmentType is not
          (JsonPathSegmentType.FieldProjection or JsonPathSegmentType.FieldExclusion))
        throw new ArgumentException("A projection must end with [field,...] or [!field,...].", nameof(path));
      operation = operation with
      {
        Fields = segments[^1].ProjectionFields,
        Exclude = segments[^1].SegmentType == JsonPathSegmentType.FieldExclusion
      };
      segments.RemoveAt(segments.Count - 1);
    }
    foreach (var segment in segments)
    {
      if (segment.SegmentType is JsonPathSegmentType.Schema or JsonPathSegmentType.NestedQuery
          or JsonPathSegmentType.FieldProjection or JsonPathSegmentType.FieldExclusion
          or JsonPathSegmentType.FieldCount or JsonPathSegmentType.FieldExistence)
        throw new ArgumentException("Editing paths must address source values, not computed extraction results.", nameof(path));
    }
    if (segments.Count == 0 && operation.Kind == JsonPathEditKind.Remove)
      throw new ArgumentException("The root document cannot be removed. Replace it with JSON null instead.", nameof(path));
    return segments;
  }

  private Frame GetFrame(int depth)
  {
    while (_frames.Count <= depth)
      _frames.Add(new());
    return _frames[depth];
  }

  private ValueTask SymbolAsync(int index)
    => _output.WriteAsync(Symbols.AsMemory(index, 1), _cancellationToken);

  private async ValueTask HeaderAsync(string? name, bool comma)
  {
    if (comma)
      await SymbolAsync(4).ConfigureAwait(false);
    if (name is not null)
    {
      await _output.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(name), _cancellationToken).ConfigureAwait(false);
      await SymbolAsync(5).ConfigureAwait(false);
    }
  }

  private async ValueTask<bool> WriteValueAsync(JsonPathStreamReader reader, List<int> states, int depth,
    string? name, bool comma, int? knownLength = null)
  {
    _cancellationToken.ThrowIfCancellationRequested();
    var container = reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject;
    if (container && depth >= MaxDepth)
      throw new JsonException($"The maximum configured depth of {MaxDepth} has been exceeded.");
    if (states.Contains(_segments.Count))
    {
      _matches = checked(_matches + 1);
      if (_operation.Kind == JsonPathEditKind.Remove)
      {
        await reader.SkipValueAsync().ConfigureAwait(false);
        return false;
      }
      await HeaderAsync(name, comma).ConfigureAwait(false);
      if (_operation.Kind == JsonPathEditKind.Project)
        await WriteProjectionAsync(reader, depth).ConfigureAwait(false);
      else
      {
        var value = _operation.Value;
        if (_operation.Kind == JsonPathEditKind.Callback)
          value = _operation.Callback!(await ReadCallbackValueAsync(reader).ConfigureAwait(false));
        else
          await reader.SkipValueAsync().ConfigureAwait(false);
        await WriteNodeAsync(value, depth).ConfigureAwait(false);
      }
      return true;
    }
    if (states.Count == 0 || !container)
    {
      await HeaderAsync(name, comma).ConfigureAwait(false);
      await reader.CopyValueAsync(_output).ConfigureAwait(false);
      return true;
    }
    var isArray = reader.TokenType == JsonTokenType.StartArray;
    if (isArray && !knownLength.HasValue && RequiresLength(states))
    {
      using var value = await reader.CaptureValueAsync().ConfigureAwait(false);
      using var replay = new JsonPathStreamReader(value.Stream, _cancellationToken, reader.Options);
      if (!await replay.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing captured array.");
      return await WriteValueAsync(replay, states, depth, name, comma, value.ArrayLength).ConfigureAwait(false);
    }
    await HeaderAsync(name, comma).ConfigureAwait(false);
    await SymbolAsync(isArray ? 2 : 0).ConfigureAwait(false);
    var index = 0;
    var written = false;
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
      if (reader.TokenType == (isArray ? JsonTokenType.EndArray : JsonTokenType.EndObject))
      {
        await SymbolAsync(isArray ? 3 : 1).ConfigureAwait(false);
        return true;
      }
      string? property = null;
      if (!isArray)
      {
        if (reader.TokenType != JsonTokenType.PropertyName)
          throw new JsonException("Expected a JSON property.");
        property = reader.PropertyName!;
        if (!await reader.ReadAsync().ConfigureAwait(false))
          throw new JsonException("Missing JSON property value.");
      }
      var child = GetFrame(depth + 1);
      SelectChild(states, child, isArray, property, index, knownLength);
      bool emitted;
      if (child.Filters.Count > 0 && !child.States.Contains(_segments.Count))
        emitted = await FilterChildAsync(reader, child, depth + 1, property, written).ConfigureAwait(false);
      else
        emitted = await WriteValueAsync(reader, child.States, depth + 1, property, written).ConfigureAwait(false);
      written |= emitted;
      index = checked(index + 1);
    }
    throw new JsonException("Incomplete JSON container.");
  }

  private void SelectChild(List<int> states, Frame child, bool array, string? name, int index, int? length)
  {
    child.States.Clear();
    child.Filters.Clear();
    foreach (var state in states)
    {
      var segment = _segments[state];
      var selected = false;
      switch (segment.SegmentType)
      {
        case JsonPathSegmentType.Property:
          selected = !array && segment.PropertyName == name;
          break;
        case JsonPathSegmentType.PropertyUnion:
          selected = !array && Array.IndexOf(segment.PropertyUnionNames ?? Array.Empty<string>(), name) >= 0;
          break;
        case JsonPathSegmentType.Wildcard:
          selected = true;
          break;
        case JsonPathSegmentType.ArrayIndex:
        case JsonPathSegmentType.ArrayRange:
        case JsonPathSegmentType.ArrayUnion:
        case JsonPathSegmentType.ComputedIndex:
          selected = array && MatchesIndex(segment, index, length);
          break;
        case JsonPathSegmentType.Filter:
          AddState(child.Filters, state);
          break;
        case JsonPathSegmentType.RecursiveDescent:
          AddState(child.States, state);
          selected = array
            ? segment.PropertyName is null && MatchesIndex(segment, index, length)
            : segment.PropertyName == "*" || segment.PropertyName == name;
          break;
      }
      if (selected)
        AddState(child.States, state + 1);
    }
  }

  private static void AddState(List<int> states, int value)
  {
    if (!states.Contains(value))
      states.Add(value);
  }

  private bool RequiresLength(List<int> states)
  {
    foreach (var state in states)
    {
      var segment = _segments[state];
      if (segment.SegmentType == JsonPathSegmentType.ComputedIndex)
        return true;
      if (segment.SegmentType == JsonPathSegmentType.ArrayUnion)
      {
        foreach (var index in segment.ArrayUnionIndices ?? Array.Empty<int>())
        {
          if (index < 0)
            return true;
        }
      }
      if (segment.SegmentType is JsonPathSegmentType.ArrayIndex or JsonPathSegmentType.ArrayRange
          || segment.SegmentType == JsonPathSegmentType.RecursiveDescent && segment.PropertyName is null)
      {
        if (segment.ArrayIndex < 0 || segment.ArrayRangeEnd < 0 && segment.ArrayRangeEnd != int.MinValue
            && segment.SegmentType != JsonPathSegmentType.ArrayIndex)
          return true;
      }
    }
    return false;
  }

  private static bool MatchesIndex(JsonPathSegment segment, int index, int? length)
  {
    if (segment.SegmentType == JsonPathSegmentType.ArrayUnion)
    {
      foreach (var requested in segment.ArrayUnionIndices ?? Array.Empty<int>())
      {
        if (index == (requested < 0 ? length!.Value + requested : requested))
          return true;
      }
      return false;
    }
    var start = segment.ArrayIndex;
    if (segment.SegmentType == JsonPathSegmentType.ComputedIndex
        && !JsonPathComputedExpressionEvaluator.TryEvaluateIndex(length!.Value,
          segment.ComputedIndexExpression ?? string.Empty, out start))
      return false;
    if (start < 0)
      start = length!.Value + start;
    var range = segment.SegmentType == JsonPathSegmentType.ArrayRange
      || segment.SegmentType == JsonPathSegmentType.RecursiveDescent && segment.ArrayRangeEnd != int.MinValue;
    if (!range)
      return index == start;
    var end = segment.ArrayRangeEnd < 0 ? length!.Value + segment.ArrayRangeEnd : segment.ArrayRangeEnd;
    return index >= Math.Max(0, start) && index < end;
  }

  private async ValueTask<bool> FilterChildAsync(
    JsonPathStreamReader reader, Frame frame, int depth, string? name, bool comma)
  {
    var first = frame.Filters[0];
    var dependencies = frame.Filters.Count == 1 ? _dependencies[first]! : new JsonPathFilterDependencies();
    var canProject = _canProject[first];
    if (frame.Filters.Count > 1)
    {
      canProject = true;
      foreach (var state in frame.Filters)
        canProject &= _filters[state]!.CollectDependencies(dependencies);
    }
    using var value = await reader.CaptureValueAsync(canProject ? dependencies : null).ConfigureAwait(false);
    JsonDocument? document = null;
    JsonNode? candidate = value.FilterCandidate;
    try
    {
      if (!value.HasFilterCandidate)
      {
        if (canProject)
        {
          using var input = new JsonPathStreamReader(value.Stream, _cancellationToken, reader.Options);
          if (!await input.ReadAsync().ConfigureAwait(false))
            throw new JsonException("Missing filter candidate.");
          candidate = await dependencies.ReadAsync(input).ConfigureAwait(false);
        }
        else
          document = await JsonDocument.ParseAsync(value.Stream,
            new JsonDocumentOptions { MaxDepth = reader.Options.MaxDepth }, _cancellationToken).ConfigureAwait(false);
      }
      foreach (var state in frame.Filters)
      {
        var accepted = document is null
          ? _filters[state]!.Evaluate(candidate) : _filters[state]!.Evaluate(document.RootElement);
        if (accepted)
          AddState(frame.States, state + 1);
      }
    }
    finally
    {
      document?.Dispose();
    }
    value.Rewind();
    using var replay = new JsonPathStreamReader(value.Stream, _cancellationToken, reader.Options);
    if (!await replay.ReadAsync().ConfigureAwait(false))
      throw new JsonException("Missing filter candidate.");
    return await WriteValueAsync(replay, frame.States, depth, name, comma, value.ArrayLength).ConfigureAwait(false);
  }

  private async ValueTask<JsonNode?> ReadCallbackValueAsync(JsonPathStreamReader reader)
  {
    if (_options.MaxMaterializedValueBytes is long limit)
    {
      using var value = await reader.CaptureValueAsync(maxBytes: limit).ConfigureAwait(false);
      return await JsonNode.ParseAsync(value.Stream,
        documentOptions: new JsonDocumentOptions { MaxDepth = reader.Options.MaxDepth },
        cancellationToken: _cancellationToken).ConfigureAwait(false);
    }
    using var document = await reader.ReadValueAsync().ConfigureAwait(false);
    return JsonPathElementMatcher.Materialize(document.RootElement);
  }

  private async ValueTask WriteNodeAsync(JsonNode? value, int depth)
  {
    if (depth >= MaxDepth && value is JsonObject or JsonArray)
      throw new JsonException("The edited value would exceed the maximum configured depth.");
    var frame = GetFrame(depth);
    var buffer = frame.NodeBuffer ??= new ArrayBufferWriter<byte>(256);
    var writer = frame.NodeWriter ??= new Utf8JsonWriter(buffer,
      new JsonWriterOptions { MaxDepth = Math.Max(1, MaxDepth - depth) });
    writer.Reset(buffer);
    if (value is null)
      writer.WriteNullValue();
    else
      value.WriteTo(writer);
    writer.Flush();
    await _output.WriteAsync(buffer.WrittenMemory, _cancellationToken).ConfigureAwait(false);
    if (buffer.Capacity > 65536)
    {
      writer.Dispose();
      frame.NodeWriter = null;
      frame.NodeBuffer = null;
    }
    else
      buffer.Clear();
  }

  private async ValueTask WriteProjectionAsync(JsonPathStreamReader reader, int depth)
  {
    var array = reader.TokenType == JsonTokenType.StartArray;
    if (!array && reader.TokenType != JsonTokenType.StartObject)
    {
      await reader.CopyValueAsync(_output).ConfigureAwait(false);
      return;
    }
    await SymbolAsync(array ? 2 : 0).ConfigureAwait(false);
    var written = false;
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
      if (reader.TokenType == (array ? JsonTokenType.EndArray : JsonTokenType.EndObject))
      {
        await SymbolAsync(array ? 3 : 1).ConfigureAwait(false);
        return;
      }
      if (array)
      {
        if (written)
          await SymbolAsync(4).ConfigureAwait(false);
        if (reader.TokenType == JsonTokenType.StartObject)
          await WriteProjectionAsync(reader, depth + 1).ConfigureAwait(false);
        else
          await reader.CopyValueAsync(_output).ConfigureAwait(false);
        written = true;
        continue;
      }
      if (reader.TokenType != JsonTokenType.PropertyName)
        throw new JsonException("Expected a JSON property.");
      var name = reader.PropertyName!;
      if (!await reader.ReadAsync().ConfigureAwait(false))
        throw new JsonException("Missing JSON property value.");
      var keep = (Array.IndexOf(_operation.Fields ?? Array.Empty<string>(), name) >= 0) != _operation.Exclude;
      if (!keep)
      {
        await reader.SkipValueAsync().ConfigureAwait(false);
        continue;
      }
      await HeaderAsync(name, written).ConfigureAwait(false);
      await reader.CopyValueAsync(_output).ConfigureAwait(false);
      written = true;
    }
    throw new JsonException("Incomplete projection container.");
  }
}
