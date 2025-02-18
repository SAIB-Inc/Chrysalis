```

BenchmarkDotNet v0.14.0, Ubuntu 22.04.3 LTS (Jammy Jellyfish) WSL
12th Gen Intel Core i5-12400, 1 CPU, 12 logical and 6 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2
  Job-BZGSBA : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean     | Error    | StdDev   | Min      | Max      | Code Size | Gen0      | Gen1     | Allocated |
|------- |---------:|---------:|---------:|---------:|---------:|----------:|----------:|---------:|----------:|
| New    | 13.26 ms | 0.404 ms | 0.267 ms | 12.83 ms | 13.61 ms |     226 B |  953.1250 | 437.5000 |   8.51 MB |
| Old    | 36.84 ms | 2.610 ms | 1.726 ms | 33.98 ms | 39.19 ms |     154 B | 1454.5455 |        - |  13.01 MB |
