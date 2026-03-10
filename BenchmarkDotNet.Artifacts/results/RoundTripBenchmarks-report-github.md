```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 9900X3D 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4

Categories=RoundTrip Babbage 160KB  

```
| Method | Mean      | Error    | StdDev   | Gen0    | Gen1   | Allocated |
|------- |----------:|---------:|---------:|--------:|-------:|----------:|
| V1     | 291.95 μs | 3.097 μs | 2.897 μs | 12.2070 | 3.9063 | 600.49 KB |
| V2     |  51.40 μs | 0.595 μs | 0.557 μs |  1.5869 |      - |  80.08 KB |
