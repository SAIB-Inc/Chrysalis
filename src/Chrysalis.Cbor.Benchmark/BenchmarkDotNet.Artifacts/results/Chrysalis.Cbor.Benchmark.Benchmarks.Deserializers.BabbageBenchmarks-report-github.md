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
| New    |  4.253 ms | 0.1225 ms | 0.0810 ms |  4.064 ms |  4.327 ms |     226 B | 414.0625 | 156.2500 |   3.71 MB |
| Old    | 11.442 ms | 1.0979 ms | 0.7262 ms | 10.342 ms | 12.504 ms |     226 B | 625.0000 |  62.5000 |   5.64 MB |
