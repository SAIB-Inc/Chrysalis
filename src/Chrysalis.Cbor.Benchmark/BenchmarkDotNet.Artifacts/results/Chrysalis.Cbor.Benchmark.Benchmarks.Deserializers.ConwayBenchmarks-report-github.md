```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.1 LTS (Noble Numbat) WSL
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 9.0.102
  [Host]     : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2 DEBUG
  Job-ONJMYK : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean      | Error     | StdDev    | Min       | Max       | Code Size | Gen0     | Gen1    | Allocated |
|------- |----------:|----------:|----------:|----------:|----------:|----------:|---------:|--------:|----------:|
| New    | 0.8472 ms | 0.0178 ms | 0.0106 ms | 0.8293 ms | 0.8614 ms |     226 B |  66.4063 |  7.8125 |    1.1 MB |
| Old    | 2.4368 ms | 0.0546 ms | 0.0325 ms | 2.3870 ms | 2.4769 ms |     226 B | 101.5625 | 23.4375 |   1.66 MB |
