```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.1 LTS (Noble Numbat) WSL
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 9.0.102
  [Host]     : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2 DEBUG
  Job-ONJMYK : .NET 9.0.1 (9.0.124.61010), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean     | Error    | StdDev   | Min      | Max      | Code Size | Gen0     | Gen1     | Allocated |
|------- |---------:|---------:|---------:|---------:|---------:|----------:|---------:|---------:|----------:|
| New    | 11.23 ms | 0.070 ms | 0.046 ms | 11.18 ms | 11.32 ms |     226 B | 531.2500 | 250.0000 |    8.5 MB |
| Old    | 30.86 ms | 0.469 ms | 0.310 ms | 30.50 ms | 31.45 ms |     226 B | 812.5000 |        - |     13 MB |
