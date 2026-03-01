```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 9900X3D 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.102
  [Host]     : .NET 10.0.2 (10.0.2, 10.0.225.61305), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.2 (10.0.2, 10.0.225.61305), X64 RyuJIT x86-64-v4


```
| Method   | Mean         | Error      | StdDev     | Gen0     | Gen1     | Gen2   | Allocated   |
|--------- |-------------:|-----------:|-----------:|---------:|---------:|-------:|------------:|
| Byron1   |           NA |         NA |         NA |       NA |       NA |     NA |          NA |
| Byron7   |           NA |         NA |         NA |       NA |       NA |     NA |          NA |
| Genesis  |           NA |         NA |         NA |       NA |       NA |     NA |          NA |
| Shelley1 |     27.32 μs |   0.438 μs |   0.388 μs |   0.7935 |        - |      - |    39.31 KB |
| Allegra1 |     29.64 μs |   0.345 μs |   0.269 μs |   0.8240 |        - |      - |    40.58 KB |
| Mary1    |  3,581.89 μs |  46.765 μs |  39.051 μs |  31.2500 |   7.8125 |      - |  1891.86 KB |
| Alonzo1  |    327.54 μs |   6.416 μs |   9.405 μs |   3.9063 |        - |      - |   204.74 KB |
| Alonzo14 |  6,379.73 μs | 126.406 μs | 145.570 μs |  70.3125 |  23.4375 | 7.8125 |  3643.39 KB |
| Babbage1 |    111.46 μs |   1.627 μs |   1.671 μs |   1.2207 |        - |      - |    70.34 KB |
| Babbage9 | 19,291.25 μs | 377.107 μs | 575.882 μs | 187.5000 | 156.2500 |      - | 10561.95 KB |
| Conway1  |    131.73 μs |   2.634 μs |   2.705 μs |   1.4648 |        - |      - |    81.87 KB |

Benchmarks with issues:
  BlockDeserializationBenchmarks.Byron1: DefaultJob
  BlockDeserializationBenchmarks.Byron7: DefaultJob
  BlockDeserializationBenchmarks.Genesis: DefaultJob
