using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal sealed class PrefixReadStream(byte[] prefix, Stream inner) : Stream
{
  private int _position;

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

  public override int Read(Span<byte> buffer)
  {
    if (_position >= prefix.Length)
      return inner.Read(buffer);

    var count = Math.Min(buffer.Length, prefix.Length - _position);
    prefix.AsSpan(_position, count).CopyTo(buffer);
    _position += count;
    return count;
  }

  public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

  public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (_position >= prefix.Length)
      return inner.ReadAsync(buffer, cancellationToken);

    var count = Math.Min(buffer.Length, prefix.Length - _position);
    prefix.AsMemory(_position, count).CopyTo(buffer);
    _position += count;
    return ValueTask.FromResult(count);
  }

  public override void Flush() => throw new NotSupportedException();
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
