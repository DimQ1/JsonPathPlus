using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal sealed class JsonPathStreamReader(Stream stream, CancellationToken cancellationToken) : IDisposable
{
  private byte[] _buffer = ArrayPool<byte>.Shared.Rent(16384);
  private int _offset;
  private int _length;
  private bool _final;
  private bool _started;
  private JsonReaderState _state;
  private int _tokenOffset;
  private JsonReaderState _tokenState;

  public JsonTokenType TokenType { get; private set; }
  public int Depth { get; private set; }
  public string? PropertyName { get; private set; }

  public async ValueTask<bool> ReadAsync()
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!_started)
    {
      while (_length < 3 && !_final)
        await FillAsync().ConfigureAwait(false);

      if (_length >= 3 && _buffer[0] == 0xEF && _buffer[1] == 0xBB && _buffer[2] == 0xBF)
        _offset = 3;
      _started = true;
    }

    while (true)
    {
      if (TryRead())
        return true;
      if (_final)
        return false;
      await FillAsync().ConfigureAwait(false);
    }
  }

  private bool TryRead()
  {
    var reader = new Utf8JsonReader(_buffer.AsSpan(_offset, _length - _offset), _final, _state);
    _tokenOffset = _offset;
    _tokenState = _state;
    if (!reader.Read())
    {
      _offset += (int)reader.BytesConsumed;
      _state = reader.CurrentState;
      return false;
    }

    TokenType = reader.TokenType;
    Depth = reader.CurrentDepth;
    PropertyName = TokenType == JsonTokenType.PropertyName ? reader.GetString() : null;
    _offset += (int)reader.BytesConsumed;
    _state = reader.CurrentState;
    return true;
  }

  public async ValueTask<JsonDocument> ReadValueAsync()
  {
    var startOffset = _tokenOffset;
    var startState = _tokenState;
    if (TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
    {
      var scanOffset = _offset;
      var scanState = _state;
      var depth = Depth;
      while (!ScanValue(ref scanOffset, ref scanState, depth))
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (_final)
          throw new JsonException("Incomplete JSON value.");

        // Retain this candidate, but scan new tokens only once across short reads.
        _offset = startOffset;
        await FillAsync().ConfigureAwait(false);
        scanOffset -= startOffset;
        startOffset = 0;
      }
    }

    cancellationToken.ThrowIfCancellationRequested();
    _offset = startOffset;
    _state = startState;
    return ParseValue();
  }

  private bool ScanValue(ref int offset, ref JsonReaderState state, int depth)
  {
    var reader = new Utf8JsonReader(_buffer.AsSpan(offset, _length - offset), _final, state);
    while (reader.Read())
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (reader.CurrentDepth == depth && reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
      {
        offset += (int)reader.BytesConsumed;
        state = reader.CurrentState;
        return true;
      }
    }
    offset += (int)reader.BytesConsumed;
    state = reader.CurrentState;
    return false;
  }

  private JsonDocument ParseValue()
  {
    var reader = new Utf8JsonReader(_buffer.AsSpan(_offset, _length - _offset), _final, _state);
    if (!reader.Read())
      throw new JsonException("Missing JSON value.");
    var document = JsonDocument.ParseValue(ref reader);

    TokenType = reader.TokenType;
    Depth = reader.CurrentDepth;
    _offset += (int)reader.BytesConsumed;
    _state = reader.CurrentState;
    return document;
  }

  public async ValueTask SkipValueAsync()
  {
    if (TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject))
      return;

    var depth = Depth;
    while (await ReadAsync().ConfigureAwait(false))
    {
      if (Depth == depth && TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
        return;
    }

    throw new JsonException("Incomplete JSON container.");
  }

  private async ValueTask FillAsync()
  {
    var remaining = _length - _offset;
    if (remaining == _buffer.Length)
    {
      var larger = ArrayPool<byte>.Shared.Rent(checked(_buffer.Length * 2));
      _buffer.AsSpan(_offset, remaining).CopyTo(larger);
      ArrayPool<byte>.Shared.Return(_buffer);
      _buffer = larger;
    }
    else if (remaining > 0)
    {
      _buffer.AsSpan(_offset, remaining).CopyTo(_buffer);
    }

    _offset = 0;
    _length = remaining;
    var read = await stream.ReadAsync(_buffer.AsMemory(remaining), cancellationToken).ConfigureAwait(false);
    _length += read;
    _final = read == 0;
  }

  public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}
