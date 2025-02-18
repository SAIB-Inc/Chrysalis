```

BenchmarkDotNet v0.14.0, Ubuntu 22.04.3 LTS (Jammy Jellyfish) WSL
12th Gen Intel Core i5-12400, 1 CPU, 12 logical and 6 physical cores
.NET SDK 9.0.100
  [Host]     : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2
  Job-BZGSBA : .NET 9.0.0 (9.0.24.52809), X64 RyuJIT AVX2

IterationCount=10  MaxIterationCount=20  UnrollFactor=1  
WarmupCount=3  

```
| Method | Mean      | Error     | StdDev    | Min       | Max       | Code Size | Gen0     | Gen1     | Allocated |
|------- |----------:|----------:|----------:|----------:|----------:|----------:|---------:|---------:|----------:|
| New    |  4.509 ms | 0.1629 ms | 0.1077 ms |  4.348 ms |  4.702 ms |     226 B | 515.6250 | 187.5000 |   4.64 MB |
| Old    | 15.538 ms | 0.5237 ms | 0.3464 ms | 15.237 ms | 16.221 ms |     226 B | 906.2500 |  62.5000 |   8.04 MB |
