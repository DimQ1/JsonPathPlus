using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JsonPathPlus;

internal sealed class JsonPathBufferedWriteStream(Stream destination) : Stream
{
  private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(16384);
  private int _count;
  private bool _disposed;
  public override bool CanRead => false;
  public override bool CanSeek => false;
  public override bool CanWrite => !_disposed;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (buffer.Length > _buffer.Length - _count)
      await DrainAsync(cancellationToken).ConfigureAwait(false);
    if (buffer.Length >= _buffer.Length)
      await destination.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    else
    {
      buffer.CopyTo(_buffer.AsMemory(_count));
      _count += buffer.Length;
    }
  }
  public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
  private async ValueTask DrainAsync(CancellationToken cancellationToken)
  {
    if (_count > 0)
    {
      await destination.WriteAsync(_buffer.AsMemory(0, _count), cancellationToken).ConfigureAwait(false);
      _count = 0;
    }
  }
  public override async Task FlushAsync(CancellationToken cancellationToken)
  {
    await DrainAsync(cancellationToken).ConfigureAwait(false);
    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
  }
  protected override void Dispose(bool disposing)
  {
    if (disposing && !_disposed)
    {
      _disposed = true;
      ArrayPool<byte>.Shared.Return(_buffer);
    }
    base.Dispose(disposing);
  }
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  public override void Flush() => throw new NotSupportedException();
  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
}
