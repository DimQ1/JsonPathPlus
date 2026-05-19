```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7840)
Unknown processor
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method                        | Mean      | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|------------------------------ |----------:|----------:|----------:|--------:|-------:|----------:|
| Stream_First_SimpleProperty   |  45.61 μs |  0.900 μs |  1.755 μs |  2.0752 |      - |  13.23 KB |
| Stream_All_Range              | 430.99 μs |  8.203 μs |  8.056 μs | 10.2539 |      - |  64.63 KB |
| Stream_RootArray_First_Index  | 303.96 μs |  5.947 μs |  6.364 μs | 19.5313 | 4.3945 | 121.38 KB |
| Stream_RootArray_All_Wildcard | 665.85 μs | 13.154 μs | 23.381 μs | 54.6875 | 7.8125 | 336.34 KB |
