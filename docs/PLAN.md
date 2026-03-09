# Chrysalis.Plutus Implementation Plan

Port of [blaze-plutus](https://github.com/butaneprotocol/blaze-cardano/tree/main/packages/blaze-plutus) (TypeScript) and [blaze-uplc](https://github.com/butaneprotocol/blaze-cardano/tree/main/packages/blaze-uplc) to pure managed C#. Eliminates all Rust FFI / native dependencies.

**Spec**: `docs/plutus-core-spec.pdf` (Plutus Core Specification, 18 December 2025 DRAFT, 78 pages)

---

## Goals

1. **Zero native dependencies** — pure .NET 10, no P/Invoke, no WASM
2. **Standalone** — depends only on BCL + BouncyCastle.Cryptography + Chrysalis.Crypto (all pure managed C#)
3. **Well-documented** — visual high-level overview of how UPLC works, with C#-specific implementation details
4. **Spec-conformant** — faithful to the Plutus Core formal specification
5. **Drop-in replacement** — same public API surface as existing Chrysalis.Plutus (EvaluationResult, ExUnits, etc.)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Chrysalis.Plutus                       │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌──────────┐   ┌───────────┐   ┌────────────────────┐  │
│  │  Script   │──▸│   Flat    │──▸│   Program<DeBruijn>│  │
│  │  (bytes)  │   │  Decoder  │   │   (AST)            │  │
│  └──────────┘   └───────────┘   └────────┬───────────┘  │
│                                           │              │
│                       ┌───────────────────▼───────┐      │
│                       │       CEK Machine         │      │
│                       │  ┌─────────────────────┐  │      │
│                       │  │ Compute ▷  Return ◁ │  │      │
│                       │  │ Error ◆    Halt □V  │  │      │
│                       │  └─────────────────────┘  │      │
│                       │         │                 │      │
│                       │  ┌──────▼──────┐          │      │
│                       │  │  Builtins   │          │      │
│                       │  │  (106+)     │          │      │
│                       │  └──────┬──────┘          │      │
│                       │         │                 │      │
│                       │  ┌──────▼──────┐          │      │
│                       │  │  Cost Model │          │      │
│                       │  │  (budget)   │          │      │
│                       │  └─────────────┘          │      │
│                       └───────────────────────────┘      │
│                                                          │
│  ┌──────────┐   ┌───────────┐   ┌────────────────────┐  │
│  │  Text    │──▸│  Parser   │──▸│  Program<Name>     │  │
│  │  (.uplc) │   │  + Lexer  │   │  ──▸ DeBruijn conv │  │
│  └──────────┘   └───────────┘   └────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Crypto (via BCL + BouncyCastle + Chrysalis.Crypto) │  │
│  │  SHA-256, SHA3-256, Blake2b-{224,256}, Keccak-256, │  │
│  │  RIPEMD-160, Ed25519, secp256k1 (BouncyCastle)    │  │
│  │  BIP-340 Schnorr (manual, BouncyCastle EC prims)  │  │
│  │  BLS12-381 (Chrysalis.Crypto, ported noble-curves)│  │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ┌──────────────────────────┐                            │
│  │  Minimal CBOR codec      │  (PlutusData only,        │
│  │  ~200 lines, Appendix B) │   for serialiseData)      │
│  └──────────────────────────┘                            │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Dependencies

| Dependency | Purpose | Native? |
|---|---|---|
| .NET 10 BCL | SHA-256, SHA3-256, SHA-512 | No (managed) |
| BouncyCastle.Cryptography | Ed25519, Blake2b, Keccak-256, RIPEMD-160, secp256k1 (ECDSA + EC primitives for Schnorr) | No (pure managed) |
| Chrysalis.Crypto | BLS12-381 pairing (ported from noble-curves TypeScript) | No (pure managed) |

**Note**: BouncyCastle C# 2.6.1 has NO BLS12-381 support and NO BIP-340 Schnorr signer.
- BIP-340 Schnorr: implemented manually in `Bip340Schnorr.cs` using BouncyCastle's secp256k1 EC primitives
- BLS12-381: requires `Chrysalis.Crypto` — a pure managed port of [noble-curves](https://github.com/paulmillr/noble-curves) BLS12-381

---

## Phases

### Phase 0: Project Setup & Documentation ✓
- [x] Create git worktree (`feature/plutus-managed`)
- [x] Copy Plutus Core spec to `docs/plutus-core-spec.pdf`
- [ ] Write `docs/UPLC.md` — visual high-level overview of how UPLC works with C#-specific details
- [x] Rewrite `Chrysalis.Plutus.csproj` — remove all native/FFI targets, add BouncyCastle.Cryptography
- [x] Delete old FFI code (`VM/Interop/`, `Dependencies/`, `lib/`, `build-rs.sh`)

### Phase 1: Core Types ✓
Source: `blaze-plutus/src/types.ts` (641 lines)

| C# File | What | Notes |
|---|---|---|
| `Types/Term.cs` | `Term<TBinder>` discriminated union | 10 variants: Var, Lambda, Apply, Constant, Builtin, Delay, Force, Constr, Case, Error. Use abstract record + sealed subtypes. |
| `Types/Constant.cs` | `Constant` discriminated union | 12 variants: Integer, ByteString, String, Bool, Unit, List, Pair, Data, Bls12_381_G1, Bls12_381_G2, Bls12_381_MlResult, Array |
| `Types/ConstantType.cs` | `ConstantType` for Flat encoding type tags | Recursive: base types + list/array/pair constructors |
| `Types/PlutusData.cs` | `PlutusData` 5-variant recursive type | Constr, Map, List, Integer, ByteString |
| `Types/DefaultFunction.cs` | `enum DefaultFunction` (0-93) | 94 builtins across 6 batches, with arity/force-count metadata |
| `Types/ExBudget.cs` | `ExBudget` record (CPU + Memory) | `record struct ExBudget(long Cpu, long Mem)` |
| `Types/Program.cs` | `Program<TBinder>` with Version | Version is 3 naturals (major.minor.patch) |
| `Types/Binder.cs` | `Name` and `DeBruijn` binder types | Name has text+unique, DeBruijn has index |

### Phase 2: Flat Encoding/Decoding ✓
Source: `blaze-uplc/src/flat.ts` (131 lines), `encoder.ts`, `decoder.ts`

| C# File | What | Spec Section |
|---|---|---|
| `Flat/BitReader.cs` | Bit-level reader (popBit, popBits, popByte, takeBytes) | C.1 |
| `Flat/BitWriter.cs` | Bit-level writer (pushBit, pushBits, pushByte, pad) | C.1 |
| `Flat/FlatDecoder.cs` | Decode bytes → `Program<DeBruijn>` | C.3 |
| `Flat/FlatEncoder.cs` | Encode `Program<DeBruijn>` → bytes | C.3 |

Key encoding rules (from spec Appendix C):
- **Programs**: 3 natural-number version components + term + padding
- **Terms**: 4-bit tag (0=Var..9=Case), then subterm encodings
- **Builtins**: 7-bit integer tags (Tables C.3–C.8)
- **Constants**: type tag list (4-bit tags) + value encoding
- **Integers**: zigzag encoding → variable-length natural (7-bit blocks)
- **ByteStrings**: pad to byte boundary, then 255-byte chunks with length prefix, terminated by 0-length chunk
- **Strings**: UTF-8 → bytestring encoding
- **Data**: CBOR-encode → bytestring encoding
- **Variables (DeBruijn)**: natural number encoding (index, 1-based)

### Phase 3: CBOR Codec (PlutusData only) ✓
Source: Spec Appendix B (sections B.1–B.7)

| C# File | What | Spec Section |
|---|---|---|
| `Cbor/CborWriter.cs` | Minimal CBOR encoder for PlutusData | B.3–B.7 |
| `Cbor/CborReader.cs` | Minimal CBOR decoder for PlutusData | B.3–B.7 |

Only needs: major types 0–5 (unsigned int, negative int, bytestring, text, array, map), plus tag 6 for constructor encoding. ~200 lines total.

Constructor tag CBOR encoding (spec B.7):
- Tags 0–6: CBOR tag `121 + i`
- Tags 7–127: CBOR tag `1280 + (i - 7)`
- Tags >= 128: CBOR tag 102, then 2-element list `[tag, fields]`

### Phase 4: Parser & Text Format
Source: `blaze-plutus/src/lexer.ts`, `parse.ts`, `convert.ts`, `pretty.ts`

| C# File | What | Source |
|---|---|---|
| `Text/Lexer.cs` | Hand-written tokenizer for UPLC text syntax | `lexer.ts` |
| `Text/Parser.cs` | Recursive descent parser → `Program<Name>` | `parse.ts` |
| `Text/DeBruijnConverter.cs` | `Program<Name>` → `Program<DeBruijn>` | `convert.ts` |
| `Text/PrettyPrinter.cs` | `Term` → text string | `pretty.ts` |

### Phase 5: CEK Machine ✓
Source: `blaze-plutus/src/cek/` (machine.ts, value.ts, context.ts, discharge.ts, costing.ts, exmem.ts, error.ts, costs.ts)

| C# File | What | Spec Section |
|---|---|---|
| `Cek/CekValue.cs` | Runtime values: VConstant, VLambda, VDelay, VBuiltin, VConstr | 2.4 |
| `Cek/Environment.cs` | Linked-list environment (1-based DeBruijn lookup) | 2.4 |
| `Cek/Context.cs` | 7 continuation frame types + transferArgStack | 2.4 (Figure 2.10) |
| `Cek/Discharge.cs` | Value → Term conversion (environment substitution) | 2.4.1 (Figure 2.11) |
| `Cek/CekMachine.cs` | Main evaluation loop: Compute/Return/Done state machine | 2.4 (Figure 2.10) |
| `Cek/CostModel.cs` | Cost algebra: linear, quadratic, two-variable models | — |
| `Cek/ExMem.cs` | Size measurement for builtin costing | — |
| `Cek/MachineCosts.cs` | Per-step costs (16000 CPU / 100 MEM) + startup cost | — |
| `Cek/DefaultCosts.cs` | Default builtin cost parameters (from Cardano protocol params) | — |

CEK Machine states (spec Figure 2.10):
```
Compute ▷  (ctx, env, term)  → evaluate term in environment, push continuation
Return  ◁  (ctx, value)      → pop continuation frame, apply to value
Error   ◆                    → evaluation failed
Halt    □V                   → no more frames, return final value
```

Budget tracking: accumulate unbudgeted steps, flush every 200 steps (slippage threshold).

### Phase 6: Builtin Functions (94) — IN PROGRESS
Source: `blaze-plutus/src/cek/builtins/` (13 modules)

**Status**: 75/94 builtins fully implemented. 19 BLS12-381 builtins stubbed (throw `NotImplementedException`), pending Chrysalis.Crypto BLS port.

| C# File | Builtins | Status |
|---|---|---|
| `Builtins/BuiltinHelpers.cs` | UnwrapX/XResult helpers for all types | ✓ |
| `Builtins/BuiltinRuntime.cs` | Central dispatch: `DefaultFunction` → implementation | ✓ |
| `Builtins/IntegerBuiltins.cs` | add, subtract, multiply, divide, quotient, remainder, mod, equals, lessThan, lessThanEquals, expModInteger | ✓ |
| `Builtins/ByteStringBuiltins.cs` | append, cons, slice, length, index, equals, lessThan, lessThanEquals | ✓ |
| `Builtins/CryptoBuiltins.cs` | sha2_256, sha3_256, blake2b_256, blake2b_224, keccak_256, ripemd_160, verifyEd25519, verifyEcdsaSecp256k1, verifySchnorrSecp256k1 | ✓ |
| `Builtins/Bip340Schnorr.cs` | BIP-340 Schnorr verification (manual impl using BouncyCastle EC) | ✓ |
| `Builtins/StringBuiltins.cs` | append, equals, encodeUtf8, decodeUtf8 | ✓ |
| `Builtins/ControlBuiltins.cs` | ifThenElse, chooseUnit, trace | ✓ |
| `Builtins/ListBuiltins.cs` | mkCons, headList, tailList, nullList, chooseList, dropList | ✓ |
| `Builtins/PairBuiltins.cs` | fstPair, sndPair | ✓ |
| `Builtins/DataBuiltins.cs` | constrData, mapData, listData, iData, bData, unConstrData, unMapData, unListData, unIData, unBData, equalsData, chooseData, mkPairData, mkNilData, mkNilPairData, serialiseData | ✓ |
| `Builtins/BitwiseBuiltins.cs` | and, or, xor, complement, readBit, writeBits, replicateByte, shift, rotate, countSetBits, findFirstSetBit | ✓ |
| `Builtins/ArrayBuiltins.cs` | lengthOfArray, listToArray, indexArray | ✓ |
| `Builtins/BlsBuiltins.cs` | G1/G2 add/neg/scalarMul/equal/compress/uncompress/hashToGroup/multiScalarMul, millerLoop, mulMlResult, finalVerify | ✓ implemented via Chrysalis.Crypto.Bls12381 |

Crypto implementation mapping:
| Builtin | Implementation |
|---|---|
| sha2_256 | `Org.BouncyCastle.Crypto.Digests.Sha256Digest` |
| sha3_256 | `Org.BouncyCastle.Crypto.Digests.Sha3Digest` |
| blake2b_256, blake2b_224 | `Org.BouncyCastle.Crypto.Digests.Blake2bDigest` |
| keccak_256 | `Org.BouncyCastle.Crypto.Digests.KeccakDigest` |
| ripemd_160 | `Org.BouncyCastle.Crypto.Digests.RipeMD160Digest` |
| verifyEd25519Signature | `Org.BouncyCastle.Crypto.Signers.Ed25519Signer` |
| verifyEcdsaSecp256k1Signature | `Org.BouncyCastle.Crypto.Signers.ECDsaSigner` + BIP-146 low-s check |
| verifySchnorrSecp256k1Signature | `Bip340Schnorr.Verify()` (manual BIP-340 using BouncyCastle EC prims) |
| bls12_381_* | `Chrysalis.Crypto.Bls12381.Bls12381` (ported from noble-curves) |

### Phase 6a: Chrysalis.Crypto BLS12-381 Port — ✓ COMPLETE
Ported noble-curves TypeScript BLS12-381 to pure managed C# in `Chrysalis.Crypto`.

Files created in `src/Chrysalis.Crypto/Bls12381/`:

| C# File | What It Does |
|---|---|
| `FpUtils.cs` | Shared utilities: PosMod, NumberToBytesBE, BytesToNumberBE, BitLen, BitGet, InvertBatch |
| `Fp.cs` | Base field Fp (381-bit prime): mod, add, sub, mul, sqr, neg, inv, pow, sqrt, div, legendre |
| `Fp2.cs` | Quadratic extension Fp2 = Fp[u]/(u²+1): Karatsuba mul, optimized sqr, sqrt, FrobeniusMap |
| `Fp6.cs` | Cubic extension Fp6 = Fp2[v]/(v³-(u+1)): sparse mul (Mul01, Mul1), FrobeniusMap |
| `Fp12.cs` | Quadratic extension Fp12 = Fp6[w]/(w²-v): cyclotomic ops, Mul014, final exponentiation |
| `PointG1.cs` | G1 projective point: RCB add/double, compress/uncompress (48-byte ZCash), GLV torsion check |
| `PointG2.cs` | G2 projective point over Fp2: RCB add/double, compress/uncompress (96-byte), Psi/Psi2 endomorphism, RFC 9380 cofactor clearing |
| `Pairing.cs` | Miller loop with precomputed line functions, NAF decomposition, batch pairing |
| `HashToCurve.cs` | RFC 9380: expand_message_xmd (SHA-256), hash_to_field, SWU map (Fp + Fp2), isogeny maps (11-isogeny G1, 3-isogeny G2) |
| `Bls12381.cs` | Public API facade: G1/G2 ops, MillerLoop, MulMlResult, FinalVerify, HashToGroup |

Integration: `BlsBuiltins.cs` now calls `Chrysalis.Crypto.Bls12381.Bls12381` for all 19 BLS builtins.

### Phase 7: Integration & Public API
- [ ] `Evaluator.cs` — high-level API: script bytes + params → `EvaluationResult`
- [ ] `ScriptApplicator.cs` — apply parameters to parameterized scripts (Flat decode → apply args → re-encode)
- [ ] `EvaluationResult.cs`, `ExUnits.cs`, `RedeemerTag.cs` — keep existing public models
- [ ] Wire up to existing Chrysalis transaction building pipeline
- [ ] Remove old FFI code completely

### Phase 8: Testing
- [ ] Unit tests for each phase (types, flat, cbor, parser, cek, builtins)
- [ ] Port conformance test cases from `blaze-plutus/conformance/tests/`
- [ ] Cross-validate with Rust `uplc` crate outputs
- [ ] Benchmark against old FFI implementation

---

## File Structure (final)

```
src/Chrysalis.Plutus/
├── Chrysalis.Plutus.csproj
├── Types/
│   ├── Term.cs
│   ├── Constant.cs
│   ├── ConstantType.cs
│   ├── PlutusData.cs
│   ├── DefaultFunction.cs
│   ├── ExBudget.cs
│   ├── Program.cs
│   └── Binder.cs
├── Flat/
│   ├── BitReader.cs
│   ├── BitWriter.cs
│   ├── FlatDecoder.cs
│   └── FlatEncoder.cs
├── Cbor/
│   ├── CborWriter.cs
│   └── CborReader.cs
├── Text/
│   ├── Lexer.cs
│   ├── Parser.cs
│   ├── DeBruijnConverter.cs
│   └── PrettyPrinter.cs
├── Cek/
│   ├── CekMachine.cs
│   ├── CekValue.cs
│   ├── Environment.cs
│   ├── Context.cs
│   ├── Discharge.cs
│   ├── CostModel.cs
│   ├── ExMem.cs
│   ├── MachineCosts.cs
│   └── DefaultCosts.cs
├── Builtins/
│   ├── BuiltinDispatch.cs
│   ├── IntegerBuiltins.cs
│   ├── ByteStringBuiltins.cs
│   ├── CryptoBuiltins.cs
│   ├── StringBuiltins.cs
│   ├── ControlBuiltins.cs
│   ├── ListBuiltins.cs
│   ├── PairBuiltins.cs
│   ├── DataBuiltins.cs
│   ├── BlsBuiltins.cs
│   ├── BitwiseBuiltins.cs
│   ├── ConversionBuiltins.cs
│   ├── ArrayBuiltins.cs
│   └── ValueBuiltins.cs
├── Evaluator.cs
└── ScriptApplicator.cs
```

---

## Execution Order

Phases build on each other:

```
Phase 0 (setup/docs)
  └─▸ Phase 1 (core types)
        ├─▸ Phase 2 (flat encoding)  ◂── needs types
        ├─▸ Phase 3 (cbor codec)     ◂── needs PlutusData
        └─▸ Phase 4 (parser/text)    ◂── needs types
              └─▸ Phase 5 (cek machine) ◂── needs types + values
                    └─▸ Phase 6 (builtins) ◂── needs cek + BouncyCastle
                          └─▸ Phase 7 (integration) ◂── needs everything
                                └─▸ Phase 8 (testing)
```

Phases 2, 3, and 4 can be developed in parallel after Phase 1 is complete.

---

## Key Design Decisions

### Record classes vs record structs
Use `readonly record struct` for small, fixed-size leaf types (stack-allocated, zero GC pressure):
- `ExBudget` — two longs
- `DeBruijn` — single int
- `Name` — string + int
- `Version` — three ints

Use `abstract record` (class) + `sealed record` subtypes for discriminated unions (requires inheritance, recursive):
- `Term<TBinder>` — 10 variants (VarTerm, LambdaTerm, ApplyTerm, etc.)
- `Constant` — 12 variants
- `PlutusData` — 5 variants
- `ConstantType` — 12 variants (base types + list/array/pair constructors)

```csharp
// Value type — stack allocated
public readonly record struct ExBudget(long Cpu, long Mem);

// Reference type — DU via inheritance
public abstract record Term<TBinder>;
public sealed record VarTerm<TBinder>(TBinder Name) : Term<TBinder>;
public sealed record LambdaTerm<TBinder>(TBinder Parameter, Term<TBinder> Body) : Term<TBinder>;
```

### BigInteger vs long
- `ExBudget`: use `long` (matches Haskell's Int64, sufficient for budget tracking)
- Plutus `integer` type: use `System.Numerics.BigInteger` (arbitrary precision required by spec)
- PlutusData integer: `BigInteger`

### Environment representation
Linked list (same as blaze-plutus) — simple, immutable-friendly, correct DeBruijn semantics.

### Cost model
Budget tracking with slippage (batch 200 steps before checking). Saturating arithmetic to avoid overflow.
