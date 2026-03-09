# UPLC CEK Machine — Informal Benchmark

Informal performance comparison of UPLC evaluators across languages.
Not a rigorous benchmark — single machine, no statistical analysis, just wall-clock averages.

## Setup

- **Machine**: WSL2 on Windows, Linux 5.15 (x86_64)
- **Methodology**: Parse UPLC text + convert to DeBruijn + evaluate, repeated N times. 1 warmup run before timing.
- **Scripts**: Two conformance test programs
  - `factorial(20)` — recursive integer arithmetic, result: 2432902008176640000
  - `evenList` — recursive list processing with Church-encoded data, result: 4

All implementations produce identical results and budgets (cpu/mem).

## Implementations

| Runtime | Language | Notes |
|---|---|---|
| **Chrysalis.Plutus** (AOT) | C# / .NET 10 | `dotnet publish -p:PublishAot=true`, native binary |
| **Chrysalis.Plutus** (JIT) | C# / .NET 10 | `dotnet build -c Release`, standard CLR |
| [plutuz](https://github.com/utxo-company/plutuz) | Zig 0.15.2 | `-Doptimize=ReleaseFast`, arena allocator per eval |
| [uplc-turbo](https://github.com/pragma-org/uplc) | Rust | `--release`, bumpalo arena allocator |
| [blaze-plutus](https://github.com/butaneprotocol/blaze-cardano) (Node) | TypeScript | Node.js v22, V8 JIT |
| [blaze-plutus](https://github.com/butaneprotocol/blaze-cardano) (Bun) | TypeScript | Bun 1.x, JavaScriptCore |
| [plutigo](https://github.com/blinklabs-io/plutigo) | Go 1.25 | Used by [gouroboros](https://github.com/blinklabs-io/gouroboros), object pooling |
| [aiken/uplc](https://github.com/aiken-lang/aiken) | Rust | `--release`, Rc/clone based (no arena) |

## Results — 100,000 iterations (true steady state)

### factorial(20)

```
┌───────────────────────────┬──────────┬──────────┐
│          Runtime          │ ms/eval  │ Relative │
├───────────────────────────┼──────────┼──────────┤
│ C# AOT (Chrysalis)        │ 0.069 ms │ 1.0x     │
├───────────────────────────┼──────────┼──────────┤
│ C# JIT (Chrysalis)        │ 0.074 ms │ 1.1x     │
├───────────────────────────┼──────────┼──────────┤
│ Zig (plutuz)              │ 0.075 ms │ 1.1x     │
├───────────────────────────┼──────────┼──────────┤
│ Rust (uplc-turbo)         │ 0.076 ms │ 1.1x     │
├───────────────────────────┼──────────┼──────────┤
│ TypeScript/Node (blaze)   │ 0.103 ms │ 1.5x     │
├───────────────────────────┼──────────┼──────────┤
│ TypeScript/Bun (blaze)    │ 0.146 ms │ 2.1x     │
├───────────────────────────┼──────────┼──────────┤
│ Rust (aiken uplc)         │ 0.187 ms │ 2.7x *   │
├───────────────────────────┼──────────┼──────────┤
│ Go (plutigo)              │ 0.236 ms │ 3.4x     │
└───────────────────────────┴──────────┴──────────┘
```

### evenList

```
┌───────────────────────────┬──────────┬──────────┐
│          Runtime          │ ms/eval  │ Relative │
├───────────────────────────┼──────────┼──────────┤
│ Zig (plutuz)              │ 0.071 ms │ 1.0x     │
├───────────────────────────┼──────────┼──────────┤
│ C# AOT (Chrysalis)        │ 0.092 ms │ 1.3x     │
├───────────────────────────┼──────────┼──────────┤
│ C# JIT (Chrysalis)        │ 0.093 ms │ 1.3x     │
├───────────────────────────┼──────────┼──────────┤
│ Rust (uplc-turbo)         │ 0.111 ms │ 1.6x     │
├───────────────────────────┼──────────┼──────────┤
│ TypeScript/Node (blaze)   │ 0.114 ms │ 1.6x     │
├───────────────────────────┼──────────┼──────────┤
│ TypeScript/Bun (blaze)    │ 0.166 ms │ 2.3x     │
├───────────────────────────┼──────────┼──────────┤
│ Rust (aiken uplc)         │ 0.192 ms │ 2.7x *   │
├───────────────────────────┼──────────┼──────────┤
│ Go (plutigo)              │ 0.201 ms │ 2.8x     │
└───────────────────────────┴──────────┴──────────┘
```

\* aiken measured at 10k iterations

## Observations

- **C# AOT and JIT converge at high iteration counts.** At 1k iterations JIT is ~2x slower due to tiered compilation warmup. By 100k they're within 5%. For sustained workloads (node validation), JIT is effectively as fast as AOT.

- **Top 4 are within noise on factorial.** C# AOT, C# JIT, Zig, and Rust uplc-turbo all land at 0.069–0.076 ms. The differences are likely within measurement noise.

- **Plutuz (Zig) leads on recursive workloads.** evenList shows Zig's arena allocator paying off — 0.071 ms vs C#'s 0.092 ms. Zig's explicit memory control shines with deep recursion and many short-lived allocations.

- **V8 is impressive.** TypeScript/blaze-plutus at 0.103 ms is faster than Rust/aiken despite being a dynamic language. V8's JIT is world-class.

- **Allocation strategy matters more than language.** Rust uplc-turbo (arena) is ~2.5x faster than Rust aiken (Rc/clone). Same language, same builtins — the arena allocator makes the difference.

- **Go is in the back.** Plutigo at 0.201–0.236 ms lands near Rust/aiken despite Go's GC and runtime overhead. Go's object pooling helps but can't match arena-based allocators.

- **Chrysalis has optimization headroom.** The C# implementation is a direct port from TypeScript with no performance tuning — no `Span<T>` in hot paths, no object pooling, no struct-based CEK frames. There's room to close the gap with Zig on recursive workloads.

## How to reproduce

```bash
# C# AOT
dotnet publish /tmp/bench-csharp -c Release -o /tmp/bench-csharp/aot --self-contained -p:PublishAot=true
/tmp/bench-csharp/aot/bench-csharp /tmp/bench-factorial.uplc 100000

# C# JIT
dotnet build /tmp/bench-csharp -c Release -o /tmp/bench-csharp/out
/tmp/bench-csharp/out/bench-csharp /tmp/bench-factorial.uplc 100000

# Zig (plutuz)
cd ~/Projects/plutuz && zig build -Doptimize=ReleaseFast
~/Projects/plutuz/zig-out/bin/bench-custom /tmp/bench-factorial.uplc 100000

# Rust (uplc-turbo)
cd /tmp/bench-rust && cargo build --release
/tmp/bench-rust/target/release/bench-rust /tmp/bench-factorial.uplc 100000

# TypeScript (blaze-plutus)
cd /tmp/bench-ts && node bench.mjs /tmp/bench-factorial.uplc 100000

# Go (plutigo)
cd /tmp/bench-go && go build -o bench-go .
/tmp/bench-go/bench-go /tmp/bench-factorial.uplc 100000

# Rust (aiken)
# swap Cargo.toml dep to aiken uplc crate and rebuild
```

Benchmark scripts are in `/tmp/bench-csharp/`, `/tmp/bench-ts/`, `/tmp/bench-rust/`.
UPLC test scripts: `/tmp/bench-factorial.uplc`, `/tmp/bench-evenList.uplc`.
