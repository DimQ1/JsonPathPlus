using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace JsonPathPlus.Tests;

public sealed class CancellationTokenTests
{
  private const string SmallObjectJson = """{ "items": [{ "id": 1 }, { "id": 2 }, { "id": 3 }] }""";

  private static string LargeArrayJson()
  {
    var sb = new StringBuilder();
    sb.Append('[');
    for (var i = 0; i < 5_000; i++)
    {
      if (i > 0) sb.Append(',');
      sb.Append("{\"id\":").Append(i).Append('}');
    }
    sb.Append(']');
    return sb.ToString();
  }

  private static Stream ToStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_Stream_PreCancelled_Throws()
  {
    using var stream = ToStream(SmallObjectJson);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => stream.ExtractFirstJsonMatchAsync("$.items[0]", cts.Token));
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_Stream_PreCancelled_Throws()
  {
    using var stream = ToStream(SmallObjectJson);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await foreach (var _ in stream.ExtractAllJsonMatchesAsync("$.items[*]", cts.Token))
      {
      }
    });
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_String_PreCancelled_Throws()
  {
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await foreach (var _ in SmallObjectJson.ExtractAllJsonMatchesAsync("$.items[*]", cts.Token))
      {
      }
    });
  }

  [Fact]
  public async Task ExtractFirstJsonMatchAsync_JsonNode_PreCancelled_Throws()
  {
    var node = JsonNode.Parse(SmallObjectJson);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => node.ExtractFirstJsonMatchAsync("$.items[0]", cts.Token));
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_JsonNode_CancelMidEnumeration_Throws()
  {
    var node = JsonNode.Parse(LargeArrayJson());
    using var cts = new CancellationTokenSource();

    var seen = 0;
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await foreach (var _ in node.ExtractAllJsonMatchesAsync("$[*]", cts.Token))
      {
        seen++;
        if (seen == 10)
          cts.Cancel();
      }
    });

    Assert.InRange(seen, 10, 11);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_Stream_LargeArray_NoCancellation_CompletesNormally()
  {
    using var stream = ToStream(LargeArrayJson());
    var count = 0;
    await foreach (var _ in stream.ExtractAllJsonMatchesAsync("$[*]"))
    {
      count++;
    }

    Assert.Equal(5_000, count);
  }

  [Fact]
  public async Task ExtractAllJsonMatchesAsync_Stream_WithCancellationViaWithCancellation_Throws()
  {
    using var stream = ToStream(LargeArrayJson());
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await foreach (var _ in stream.ExtractAllJsonMatchesAsync("$[*]").WithCancellation(cts.Token))
      {
      }
    });
  }

  [Fact]
  public async Task ExtractAllJsonMatchesWithPathsAsync_Stream_PreCancelled_Throws()
  {
    using var stream = ToStream(SmallObjectJson);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await foreach (var _ in stream.ExtractAllJsonMatchesWithPathsAsync("$.items[*]", cts.Token))
      {
      }
    });
  }
}
