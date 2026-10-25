using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal sealed class JsonPathStreamReader : IDisposable
{
  private const int BlockSize = 16384;
  private sealed class Block : ReadOnlySequenceSegment<byte>
  {
    public readonly byte[] Bytes = ArrayPool<byte>.Shared.Rent(BlockSize);
    public int Written;
    public Block? Following => (Block?)Next;
    public void Update() => Memory = Bytes.AsMemory(0, Written);
    public void Link(Block next)
    {
      next.RunningIndex = RunningIndex + Written;
      Next = next;
    }
    public void Reset()
    {
      Written = 0;
      RunningIndex = 0;
      Next = null;
      Update();
    }
  }

  private readonly Stream _stream;
  private readonly CancellationToken _cancellationToken;
  private readonly long _origin;
  private Block _head = new();
  private Block _tail;
  private Block? _spare;
  private int _headOffset;
  private long _pendingConsumed;
  private long _absoluteConsumed;
  private bool _final;
  private bool _started;
  private int _returnedBlocks;
  private JsonReaderState _state;
  private JsonReaderState _tokenState;
  private ReadOnlySequence<byte> _tokenBytes;
  private long _tokenStart;
  private long? _captureStart;
  private long? _captureLimit;
  private bool _disposed;

  public JsonPathStreamReader(Stream stream, CancellationToken cancellationToken, JsonPathExtractionOptions options = default)
  {
    options.Validate();
    Options = options;
    _stream = stream;
    _cancellationToken = cancellationToken;
    _origin = stream.CanSeek ? stream.Position : 0;
    _tail = _head;
    _state = new JsonReaderState(new JsonReaderOptions { MaxDepth = options.MaxDepth });
  }

  public JsonPathExtractionOptions Options { get; }
  public JsonTokenType TokenType { get; private set; }
  public int Depth { get; private set; }
  public string? PropertyName { get; private set; }
  private ReadOnlySequence<byte> Buffered => new(_head, _headOffset, _tail, _tail.Written);

  public async ValueTask<bool> ReadAsync(bool decodePropertyName = true)
  {
    _cancellationToken.ThrowIfCancellationRequested();
    _tokenBytes = default;
    ConsumePending();
    if (!_started)
    {
      while (Buffered.Length < 3 && !_final)
        await FillAsync().ConfigureAwait(false);
      if (HasBom())
      {
        _pendingConsumed = 3;
        ConsumePending();
      }
      _started = true;
    }

    while (true)
    {
      var read = TryRead(decodePropertyName);
      if (read)
        return true;
      ConsumePending();
      CheckPendingLimits();
      if (_final)
        return false;

      if (StartsWithQuote())
        await FillStringAsync().ConfigureAwait(false);
      else
        await FillAsync().ConfigureAwait(false);
    }
  }

  private bool HasBom()
  {
    if (Buffered.Length < 3)
      return false;
    var reader = new SequenceReader<byte>(Buffered);
    return reader.TryRead(out var a) && a == 0xEF && reader.TryRead(out var b) && b == 0xBB
      && reader.TryRead(out var c) && c == 0xBF;
  }

  private bool StartsWithQuote()
  {
    var reader = new SequenceReader<byte>(Buffered);
    return reader.TryPeek(out var value) && value == (byte)'"';
  }

  private async ValueTask FillStringAsync()
  {
    long scanned = 1;
    var escaped = false;
    while (true)
    {
      CheckPendingLimits();
      if (FindClosingQuote(ref scanned, ref escaped))
      {
        // A property name may still need its colon before Read can complete.
        await FillAsync().ConfigureAwait(false);
        return;
      }
      if (_final)
        return;
      await FillAsync().ConfigureAwait(false);
      if (FindClosingQuote(ref scanned, ref escaped))
        return;
    }
  }

  private bool FindClosingQuote(ref long scanned, ref bool escaped)
  {
    var bytes = Buffered.Slice(scanned);
    foreach (var segment in bytes)
    {
      foreach (var value in segment.Span)
      {
        scanned++;
        if (escaped)
          escaped = false;
        else if (value == (byte)'\\')
          escaped = true;
        else if (value == (byte)'"')
          return true;
      }
    }
    return false;
  }

  private bool TryRead(bool decodePropertyName)
  {
    var bytes = Buffered;
    var reader = bytes.IsSingleSegment
      ? new Utf8JsonReader(bytes.FirstSpan, _final, _state)
      : new Utf8JsonReader(bytes, _final, _state);
    _tokenState = _state;
    if (!reader.Read())
    {
      _pendingConsumed = reader.BytesConsumed;
      _state = reader.CurrentState;
      return false;
    }

    TokenType = reader.TokenType;
    Depth = reader.CurrentDepth;
    CheckTokenSize(ref reader);
    PropertyName = decodePropertyName && TokenType == JsonTokenType.PropertyName ? reader.GetString() : null;
    _tokenStart = _absoluteConsumed + reader.TokenStartIndex;
    _tokenBytes = bytes.Slice(reader.TokenStartIndex, reader.BytesConsumed - reader.TokenStartIndex);
    _pendingConsumed = reader.BytesConsumed;
    _state = reader.CurrentState;
    return true;
  }

  public bool TryReadSmallDocument(out JsonDocument? document)
  {
    document = null;
    if (Buffered.Length > 65536 || Options.MaxTokenBytes.HasValue || Options.MaxCapturedValueBytes.HasValue)
      return false;
    var bytes = Buffered;
    var reader = bytes.IsSingleSegment
      ? new Utf8JsonReader(bytes.FirstSpan, _final, _tokenState)
      : new Utf8JsonReader(bytes, _final, _tokenState);
    if (!reader.Read() || !JsonDocument.TryParseValue(ref reader, out document))
      return false;
    _pendingConsumed = reader.BytesConsumed;
    _state = reader.CurrentState;
    TokenType = reader.TokenType;
    Depth = reader.CurrentDepth;
    _tokenBytes = default;
    return true;
  }

  public async ValueTask<JsonDocument> ReadValueAsync()
  {
    if (TryReadSmallDocument(out var document))
      return document!;
    using var value = await CaptureValueAsync().ConfigureAwait(false);
    value.Rewind();
    return await JsonDocument.ParseAsync(value.Stream,
      new JsonDocumentOptions { MaxDepth = Options.MaxDepth }, _cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask<JsonPathValueBuffer> CaptureValueAsync(
    JsonPathFilterDependencies? dependencies = null, long? maxBytes = null)
  {
    var start = _tokenStart;
    var isArray = TokenType == JsonTokenType.StartArray;
    var arrayLength = 0;
    var observer = dependencies?.Observe(TokenType, Depth);
    var value = _stream.CanSeek ? null : new JsonPathValueBuffer();
    _captureStart = start;
    _captureLimit = maxBytes.HasValue && Options.MaxCapturedValueBytes.HasValue
      ? Math.Min(maxBytes.Value, Options.MaxCapturedValueBytes.Value)
      : maxBytes ?? Options.MaxCapturedValueBytes;
    try
    {
      CheckCaptureSize(_absoluteConsumed + _pendingConsumed);
      if (value is not null)
        await value.AppendAsync(_tokenBytes, _cancellationToken).ConfigureAwait(false);
      if (TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        arrayLength = await ConsumeContainerAsync(value, isArray, observer).ConfigureAwait(false);
      if (value is null)
        value = new JsonPathValueBuffer(new JsonPathReplayStream(_stream,
          _origin + start, _absoluteConsumed + _pendingConsumed - start));
      value.ArrayLength = isArray ? arrayLength : null;
      value.Options = Options;
      value.HasFilterCandidate = observer?.Supported == true;
      value.FilterCandidate = observer?.Result;
      value.Rewind();
      return value;
    }
    catch
    {
      value?.Dispose();
      throw;
    }
    finally
    {
      _captureStart = null;
      _captureLimit = null;
    }
  }

  public async ValueTask SkipValueAsync()
  {
    if (TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject))
      return;
    await ConsumeContainerAsync(null, false).ConfigureAwait(false);
  }

  public async ValueTask CopyValueAsync(Stream destination)
  {
    using var output = new JsonPathValueBuffer(destination, leaveOpen: true);
    await output.AppendAsync(_tokenBytes, _cancellationToken).ConfigureAwait(false);
    if (TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
      await ConsumeContainerAsync(output, false).ConfigureAwait(false);
  }

  private async ValueTask<int> ConsumeContainerAsync(
    JsonPathValueBuffer? capture, bool countItems, JsonPathFilterDependencies.Observer? observer = null)
  {
    var depth = Depth;
    var count = 0;
    _tokenBytes = default;
    ConsumePending();
    while (true)
    {
      _cancellationToken.ThrowIfCancellationRequested();
      var complete = ReadContainerChunk(depth, countItems, ref count, observer);
      if (capture is not null && _pendingConsumed > 0)
        await capture.AppendAsync(Buffered.Slice(0, _pendingConsumed), _cancellationToken).ConfigureAwait(false);
      if (complete)
        return count;
      ConsumePending();
      CheckPendingLimits();
      if (_final)
        throw new JsonException("Incomplete JSON container.");
      if (StartsWithQuote())
        await FillStringAsync().ConfigureAwait(false);
      else
        await FillAsync().ConfigureAwait(false);
    }
  }

  private bool ReadContainerChunk(
    int depth, bool countItems, ref int count, JsonPathFilterDependencies.Observer? observer)
  {
    var bytes = Buffered;
    var reader = bytes.IsSingleSegment
      ? new Utf8JsonReader(bytes.FirstSpan, _final, _state)
      : new Utf8JsonReader(bytes, _final, _state);
    while (reader.Read())
    {
      CheckTokenSize(ref reader);
      CheckCaptureSize(_absoluteConsumed + reader.BytesConsumed);
      if (reader.CurrentDepth == depth && reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
      {
        _pendingConsumed = reader.BytesConsumed;
        _state = reader.CurrentState;
        TokenType = reader.TokenType;
        Depth = reader.CurrentDepth;
        PropertyName = null;
        return true;
      }

      if (countItems && reader.CurrentDepth == depth + 1
          && reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject or JsonTokenType.PropertyName))
        count = checked(count + 1);
      observer?.Read(ref reader);
    }
    _pendingConsumed = reader.BytesConsumed;
    _state = reader.CurrentState;
    return false;
  }

  private void CheckTokenSize(ref Utf8JsonReader reader)
  {
    if (Options.MaxTokenBytes is long limit && reader.BytesConsumed - reader.TokenStartIndex > limit)
      throw new InvalidOperationException($"The JSON token exceeds MaxTokenBytes ({limit} bytes).");
  }

  private void CheckCaptureSize(long end)
  {
    if (_captureStart is long start && _captureLimit is long limit && end - start > limit)
      throw new InvalidOperationException(
        $"The JSON value exceeds the capture/materialization limit ({limit} bytes; MaxCapturedValueBytes or MaxMaterializedValueBytes).");
  }

  private void CheckPendingLimits()
  {
    if (Options.MaxTokenBytes is long limit && Buffered.Length > limit)
      throw new InvalidOperationException($"The JSON token exceeds MaxTokenBytes ({limit} bytes).");
    CheckCaptureSize(_absoluteConsumed + Buffered.Length);
  }

  private void ConsumePending()
  {
    var remaining = _pendingConsumed;
    _absoluteConsumed += remaining;
    _pendingConsumed = 0;
    while (remaining > 0)
    {
      var available = _head.Written - _headOffset;
      if (remaining < available || _head == _tail)
      {
        _headOffset += (int)remaining;
        break;
      }
      remaining -= available;
      var previous = _head;
      _head = previous.Following!;
      _headOffset = 0;
      if (_spare is null)
      {
        previous.Reset();
        _spare = previous;
      }
      else
        Return(previous);
    }
  }

  private async ValueTask FillAsync()
  {
    if (_final)
      return;
    if (_head == _tail && _headOffset == _head.Written)
    {
      _head.Reset();
      _headOffset = 0;
    }
    if (_tail.Written == _tail.Bytes.Length)
    {
      var next = _spare ?? new Block();
      _spare = null;
      _tail.Link(next);
      _tail = next;
    }
    var read = await _stream.ReadAsync(_tail.Bytes.AsMemory(_tail.Written), _cancellationToken).ConfigureAwait(false);
    _tail.Written += read;
    _tail.Update();
    _final = read == 0;
  }

  private void Return(Block block)
  {
    // Do not populate Shared with hundreds of blocks after one oversized token.
    if (_returnedBlocks++ < 2)
      ArrayPool<byte>.Shared.Return(block.Bytes);
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    _tokenBytes = default;
    var block = _head;
    while (block is not null)
    {
      var next = block.Following;
      Return(block);
      block = next;
    }
    if (_spare is not null)
    {
      Return(_spare);
      _spare = null;
    }
  }
}
