using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

/// <summary>
/// Measures extraction allocations without including construction of the input JSON.
/// First-match allocations should remain stable as the array grows.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class JsonPathStreamingMemoryBenchmarks
{
    private byte[] _json = Array.Empty<byte>();
    private string _path = string.Empty;

    [Params(1000, 100000)]
    public int ItemCount { get; set; }

    [Params(false, true)]
    public bool ObjectRoot { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var json = new StringBuilder();
        if (ObjectRoot)
            json.Append("{\"data\":{\"items\":");
        json.Append('[');
        for (var i = 0; i < ItemCount; i++)
        {
            if (i > 0)
                json.Append(',');
            json.Append("{\"id\":").Append(i).Append(",\"active\":true,\"name\":\"item\"}");
        }
        json.Append(']');
        if (ObjectRoot)
            json.Append("}}");
        _json = Encoding.UTF8.GetBytes(json.ToString());
        _path = ObjectRoot ? "$.data.items[?(@.id == 0)].name" : "$[?(@.id == 0)].name";
    }

    [Benchmark]
    public async Task<JsonNode?> First_Seekable()
    {
        using var stream = new MemoryStream(_json);
        return await stream.ExtractFirstJsonMatchAsync(_path);
    }

    [Benchmark]
    public async Task<JsonNode?> First_NonSeekable()
    {
        using var stream = new NonSeekableStream(_json);
        return await stream.ExtractFirstJsonMatchAsync(_path);
    }

    [Benchmark]
    public async Task<int> All_NonSeekable()
    {
        using var stream = new NonSeekableStream(_json);
        var count = 0;
        await foreach (var _ in stream.ExtractAllJsonMatchesAsync(_path))
            count++;
        return count;
    }

    private sealed class NonSeekableStream(byte[] json) : Stream
    {
        private readonly MemoryStream _inner = new(json);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
