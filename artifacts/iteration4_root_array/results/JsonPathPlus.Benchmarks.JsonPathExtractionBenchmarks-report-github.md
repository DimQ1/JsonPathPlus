```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7840)
Unknown processor
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method                        | Mean      | Error    | StdDev   | Gen0    | Gen1   | Allocated |
|------------------------------ |----------:|---------:|---------:|--------:|-------:|----------:|
| Stream_RootArray_First_Index  |  42.17 μs | 0.778 μs | 0.689 μs |  5.3101 | 0.0610 |   32.7 KB |
| Stream_RootArray_All_Wildcard | 503.08 μs | 8.110 μs | 7.189 μs | 38.0859 | 5.8594 | 236.16 KB |
