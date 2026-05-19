```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7840)
Unknown processor
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method                      | Mean      | Error    | StdDev    | Gen0    | Gen1   | Allocated |
|---------------------------- |----------:|---------:|----------:|--------:|-------:|----------:|
| Stream_First_SimpleProperty |  44.30 μs | 0.771 μs |  0.721 μs |  2.1362 |      - |  13.38 KB |
| Stream_First_ArrayIndex     | 447.22 μs | 8.643 μs | 10.614 μs |  9.7656 | 0.9766 |   62.5 KB |
| Stream_All_Range            | 396.16 μs | 5.790 μs |  4.835 μs | 10.7422 |      - |  66.19 KB |
