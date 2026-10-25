using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace JsonPathPlus;

internal sealed class JsonPathValueBuffer : IDisposable
{
  private const int MemoryThreshold = 32768;
  private Stream _stream;
  private readonly bool _adaptive;
  private readonly bool _leaveOpen;

  public JsonPathValueBuffer()
  {
    _stream = new MemoryStream();
    _adaptive = true;
  }
  public JsonPathValueBuffer(Stream stream, bool leaveOpen = false)
  {
    _stream = stream;
    _leaveOpen = leaveOpen;
  }
  public Stream Stream => _stream;
  public long Length => _stream.Length;
  public int? ArrayLength { get; set; }
  public bool HasFilterCandidate { get; set; }
  public JsonNode? FilterCandidate { get; set; }
  public JsonPathExtractionOptions Options { get; set; }

  public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
  {
    if (_stream is MemoryStream stream && stream.TryGetBuffer(out var bytes))
    {
      memory = bytes.AsMemory(0, checked((int)stream.Length));
      return true;
    }
    memory = default;
    return false;
  }

  public async ValueTask AppendAsync(ReadOnlySequence<byte> bytes, CancellationToken cancellationToken)
  {
    if (_adaptive && _stream is MemoryStream memory && memory.Length + bytes.Length > MemoryThreshold)
    {
      var file = CreateTemporaryStream();
      try
      {
        memory.Position = 0;
        await memory.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        file.Dispose();
        throw;
      }
      memory.Dispose();
      _stream = file;
    }

    while (!bytes.IsEmpty)
    {
      var position = bytes.Start;
      if (!bytes.TryGet(ref position, out var segment))
        break;
      bytes = position.GetObject() is null ? default : bytes.Slice(position);
      await _stream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
    }
  }

  public void Rewind() => _stream.Position = 0;
  public void Dispose()
  {
    if (!_leaveOpen)
      _stream.Dispose();
  }

  public static FileStream CreateTemporaryStream()
    => new(Path.Combine(Path.GetTempPath(), "jsonpathplus-" + Guid.NewGuid().ToString("N") + ".tmp"),
      FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096,
      FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
}

internal sealed class JsonPathReplayStream(Stream inner, long start, long length) : Stream
{
  private readonly long _restorePosition = inner.Position;
  private long _position;
  private bool _disposed;
  public override bool CanRead => !_disposed;
  public override bool CanSeek => !_disposed;
  public override bool CanWrite => false;
  public override long Length => length;
  public override long Position
  {
    get => _position;
    set
    {
      ArgumentOutOfRangeException.ThrowIfNegative(value);
      _position = value;
    }
  }
  public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
  public override int Read(Span<byte> buffer)
  {
    if (_position >= length)
      return 0;
    inner.Position = start + _position;
    var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, length - _position)]);
    _position += read;
    return read;
  }
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    if (_position >= length)
      return 0;
    inner.Position = start + _position;
    var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, length - _position)], cancellationToken).ConfigureAwait(false);
    _position += read;
    return read;
  }
  public override long Seek(long offset, SeekOrigin origin)
  {
    Position = origin switch
    {
      SeekOrigin.Begin => offset,
      SeekOrigin.Current => checked(_position + offset),
      SeekOrigin.End => checked(length + offset),
      _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };
    return Position;
  }
  public override void Flush() => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  protected override void Dispose(bool disposing)
  {
    if (disposing && !_disposed)
    {
      _disposed = true;
      inner.Position = _restorePosition;
    }
    base.Dispose(disposing);
  }
}
