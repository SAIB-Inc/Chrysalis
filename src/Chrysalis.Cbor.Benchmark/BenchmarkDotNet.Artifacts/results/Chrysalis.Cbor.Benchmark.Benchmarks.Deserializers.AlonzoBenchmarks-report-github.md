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
| New    |  4.081 ms | 0.1141 ms | 0.0679 ms |  3.922 ms |  4.153 ms |     226 B | 289.0625 | 117.1875 |   4.64 MB |
| Old    | 14.965 ms | 0.1619 ms | 0.0964 ms | 14.868 ms | 15.148 ms |     226 B | 500.0000 | 203.1250 |   8.04 MB |
