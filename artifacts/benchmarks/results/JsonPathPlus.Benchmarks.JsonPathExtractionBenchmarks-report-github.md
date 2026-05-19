```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7840)
Unknown processor
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method                      | Mean       | Error    | StdDev    | Median     | Gen0    | Gen1    | Gen2    | Allocated |
|---------------------------- |-----------:|---------:|----------:|-----------:|--------:|--------:|--------:|----------:|
| Stream_First_SimpleProperty |   114.0 μs |  2.27 μs |   6.13 μs |   112.6 μs |  6.8359 |  0.2441 |       - |  43.05 KB |
| Stream_First_ArrayIndex     | 1,232.8 μs | 40.10 μs | 113.10 μs | 1,187.6 μs | 70.3125 | 35.1563 | 35.1563 | 359.13 KB |
| Stream_All_Range            | 1,272.8 μs | 39.85 μs | 109.75 μs | 1,251.1 μs | 72.2656 | 35.1563 | 35.1563 | 365.21 KB |
