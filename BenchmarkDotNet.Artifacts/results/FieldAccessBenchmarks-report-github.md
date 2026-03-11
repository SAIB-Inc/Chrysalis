```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 9900X3D 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v4


```
| Method | Categories             | Mean       | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|------- |----------------------- |-----------:|----------:|----------:|--------:|-------:|----------:|
| V1     | Slot Babbage 160KB     | 280.274 μs | 1.9840 μs | 1.6567 μs | 10.2539 | 4.8828 |  532898 B |
| V2     | Slot Babbage 160KB     |  92.010 μs | 0.9754 μs | 0.9124 μs |       - |      - |     320 B |
|        |                        |            |           |           |         |        |           |
| V1     | Slot Conway 3KB        |   3.705 μs | 0.0165 μs | 0.0138 μs |  0.1526 |      - |    7888 B |
| V2     | Slot Conway 3KB        |   1.942 μs | 0.0187 μs | 0.0175 μs |  0.0038 |      - |     320 B |
|        |                        |            |           |           |         |        |           |
| V1     | TxInputs Babbage 160KB | 288.340 μs | 1.9986 μs | 1.8695 μs | 10.2539 | 4.8828 |  532898 B |
| V2     | TxInputs Babbage 160KB | 156.229 μs | 0.8063 μs | 0.7147 μs |  0.2441 |      - |   23912 B |
