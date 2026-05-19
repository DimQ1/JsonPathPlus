```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7840)
Unknown processor
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method                        | Mean      | Error     | StdDev    | Median    | Gen0    | Gen1   | Allocated |
|------------------------------ |----------:|----------:|----------:|----------:|--------:|-------:|----------:|
| Stream_First_SimpleProperty   |  46.03 μs |  1.092 μs |  3.134 μs |  44.79 μs |  2.0752 |      - |  12.95 KB |
| Stream_All_Range              | 437.43 μs |  8.907 μs | 24.975 μs | 432.22 μs | 10.2539 |      - |  64.34 KB |
| Stream_RootArray_First_Index  | 307.47 μs |  6.075 μs | 13.076 μs | 306.27 μs | 19.5313 | 3.9063 | 121.16 KB |
| Stream_RootArray_All_Wildcard | 640.71 μs | 12.812 μs | 13.709 μs | 635.30 μs | 54.6875 | 8.7891 | 336.12 KB |
