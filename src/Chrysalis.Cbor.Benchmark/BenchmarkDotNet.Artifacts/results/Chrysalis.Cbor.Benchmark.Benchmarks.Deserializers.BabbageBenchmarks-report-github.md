```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.1 LTS (Noble Numbat) WSL
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 9.0.102
  [Host]     : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2 DEBUG
  Job-ONJMYK : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean      | Error     | StdDev    | Min       | Max       | Code Size | Gen0     | Gen1     | Allocated |
|------- |----------:|----------:|----------:|----------:|----------:|----------:|---------:|---------:|----------:|
| New    |  3.815 ms | 0.0838 ms | 0.0554 ms |  3.744 ms |  3.896 ms |     226 B | 226.5625 |  93.7500 |   3.71 MB |
| Old    | 10.419 ms | 0.1258 ms | 0.0748 ms | 10.333 ms | 10.536 ms |     226 B | 343.7500 | 109.3750 |   5.63 MB |
