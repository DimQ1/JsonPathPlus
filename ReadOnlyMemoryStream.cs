using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> over a <see cref="ReadOnlyMemory{T}"/>
/// with zero additional allocations.
/// </summary>
internal sealed class ReadOnlyMemoryStream : Stream
{
  private readonly ReadOnlyMemory<byte> _memory;
  private int _position;

  public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
  {
    _memory = memory;
    _position = 0;
  }

  public override bool CanRead => true;
  public override bool CanSeek => true;
  public override bool CanWrite => false;
  public override long Length => _memory.Length;

  public override long Position
  {
    get => _position;
    set
    {
      if (value < 0 || value > _memory.Length)
        throw new ArgumentOutOfRangeException(nameof(value));
      _position = (int)value;
    }
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    var available = _memory.Length - _position;
    var bytesToCopy = Math.Min(available, count);
    if (bytesToCopy == 0) return 0;

    _memory.Span.Slice(_position, bytesToCopy).CopyTo(buffer.AsSpan(offset));
    _position += bytesToCopy;
    return bytesToCopy;
  }

  public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return Task.FromResult(Read(buffer, offset, count));
  }

  public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var available = _memory.Length - _position;
    var bytesToCopy = Math.Min(available, buffer.Length);
    if (bytesToCopy == 0) return ValueTask.FromResult(0);

    _memory.Span.Slice(_position, bytesToCopy).CopyTo(buffer.Span);
    _position += bytesToCopy;
    return ValueTask.FromResult(bytesToCopy);
  }

  public override long Seek(long offset, SeekOrigin origin)
  {
    var newPosition = origin switch
    {
      SeekOrigin.Begin => offset,
      SeekOrigin.Current => _position + offset,
      SeekOrigin.End => _memory.Length + offset,
      _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };

    if (newPosition < 0 || newPosition > _memory.Length)
      throw new ArgumentOutOfRangeException(nameof(offset));

    _position = (int)newPosition;
    return _position;
  }

  public override void Flush() { }

  public override void SetLength(long value)
    => throw new NotSupportedException("ReadOnlyMemoryStream is read-only.");

  public override void Write(byte[] buffer, int offset, int count)
    => throw new NotSupportedException("ReadOnlyMemoryStream is read-only.");

  protected override void Dispose(bool disposing)
  {
    // No managed resources to dispose.
    // The caller owns the ReadOnlyMemory<byte> lifetime.
    base.Dispose(disposing);
  }
}
