using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace JsonPathPlus.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class JsonPathEditingMemoryBenchmarks
{
    private byte[] _json = Array.Empty<byte>();

    [Params(1000, 100000)]
    public int ItemCount { get; set; }

    [Params(false, true)]
    public bool Callback { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("items");
            for (var i = 0; i < ItemCount; i++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", i);
                writer.WriteNumber("score", i % 1000);
                writer.WriteString("code", "alpha");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        _json = stream.ToArray();
    }

    [Benchmark(Baseline = true)]
    public async Task WholeDocumentDom()
    {
        using var input = new MemoryStream(_json, writable: false);
        var root = (await JsonNode.ParseAsync(input))!;
        foreach (var item in root["items"]!.AsArray())
        {
            if (Callback)
                IncrementScore(item);
            else
            {
                item!.AsObject().Remove("score");
                item.AsObject().Remove("code");
            }
        }
        await JsonSerializer.SerializeAsync(Stream.Null, root);
    }

    [Benchmark]
    public async Task Streaming()
    {
        using var input = new MemoryStream(_json, writable: false);
        if (Callback)
            await input.EditJsonAsync(Stream.Null, "$.items[*]", IncrementScore);
        else
            await input.ProjectJsonAsync(Stream.Null, "$.items[id]");
    }

    private static JsonNode? IncrementScore(JsonNode? node)
    {
        node!["score"] = node["score"]!.GetValue<int>() + 1;
        return node;
    }
}
