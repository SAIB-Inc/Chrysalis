```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 9900X3D 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4


```
| Method | Categories    | Mean         | Error       | StdDev      | Gen0    | Gen1   | Allocated |
|------- |-------------- |-------------:|------------:|------------:|--------:|-------:|----------:|
| V1     | Alonzo 140KB  |  89,194.5 ns |   484.32 ns |   429.34 ns |  4.1504 | 0.4883 |  211705 B |
| V2     | Alonzo 140KB  |  14,394.4 ns |   137.06 ns |   128.21 ns |       - |      - |         - |
|        |               |              |             |             |         |        |           |
| V1     | Babbage 160KB | 281,944.8 ns | 4,025.41 ns | 3,765.37 ns | 10.2539 | 4.8828 |  532898 B |
| V2     | Babbage 160KB |  45,884.2 ns |   414.29 ns |   387.52 ns |       - |      - |         - |
|        |               |              |             |             |         |        |           |
| V1     | Byron 19KB    |  11,820.7 ns |    86.02 ns |    80.47 ns |  0.4120 | 0.0153 |   20848 B |
| V2     | Byron 19KB    |   3,389.0 ns |    19.96 ns |    18.68 ns |       - |      - |         - |
|        |               |              |             |             |         |        |           |
| V1     | Conway 3KB    |   3,612.3 ns |    31.23 ns |    29.21 ns |  0.1564 |      - |    7888 B |
| V2     | Conway 3KB    |     720.2 ns |     1.33 ns |     1.11 ns |       - |      - |         - |
