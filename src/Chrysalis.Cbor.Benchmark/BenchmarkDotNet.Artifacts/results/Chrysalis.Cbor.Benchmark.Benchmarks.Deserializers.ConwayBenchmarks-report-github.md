```

BenchmarkDotNet v0.14.0, Ubuntu 22.04.3 LTS (Jammy Jellyfish) WSL
12th Gen Intel Core i5-12400, 1 CPU, 12 logical and 6 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2
  Job-BZGSBA : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean      | Error     | StdDev    | Min       | Max       | Code Size | Gen0     | Gen1    | Allocated |
|------- |----------:|----------:|----------:|----------:|----------:|----------:|---------:|--------:|----------:|
| New    | 0.8363 ms | 0.0455 ms | 0.0238 ms | 0.7958 ms | 0.8691 ms |     226 B | 121.0938 | 15.6250 |    1.1 MB |
| Old    | 2.5735 ms | 0.2385 ms | 0.1578 ms | 2.4192 ms | 2.8827 ms |     226 B | 179.6875 | 54.6875 |   1.67 MB |
