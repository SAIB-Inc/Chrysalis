# UPLC: Untyped Plutus Lambda Calculus

A visual guide for C# developers working on the Chrysalis.Plutus implementation.

**Reference**: `docs/plutus-core-spec.pdf` (Formal Specification of the Plutus Core Language, 18 December 2025 DRAFT)

---

## Table of Contents

1. [What is UPLC?](#1-what-is-uplc)
2. [The Language](#2-the-language)
3. [The CEK Machine](#3-the-cek-machine)
4. [Built-in Functions](#4-built-in-functions)
5. [Flat Serialization](#5-flat-serialization)
6. [CBOR for PlutusData](#6-cbor-for-plutusdata)
7. [Cost Model and Budget Tracking](#7-cost-model-and-budget-tracking)
8. [Ledger Languages](#8-ledger-languages)
9. [C# Implementation Notes](#9-c-implementation-notes)

---

## 1. What is UPLC?

UPLC (Untyped Plutus Lambda Calculus) is the **smart contract execution language** for the
Cardano blockchain. Every Plutus smart contract -- whether written in Haskell, Aiken, Helios,
or any other high-level language -- compiles down to UPLC before it can run on-chain.

```
High-Level Language        Compilation         On-Chain Format
 (Aiken, Haskell)   ------>   UPLC AST   ------>  Flat bytes wrapped in CBOR
```

Key properties:

- **Eagerly evaluated** -- arguments are reduced to values before function application (strict/call-by-value)
- **Lambda calculus** -- the only abstraction mechanism is anonymous functions (`lam`)
- **First-class functions** -- functions are values that can be passed around and returned
- **No recursion primitive** -- recursion is achieved via fixed-point combinators (Y combinator)
- **Deterministic** -- same inputs always produce same outputs, same cost
- **Budgeted** -- every operation costs CPU + memory; scripts that exceed their budget fail

### How scripts get on-chain

```
                                    On-chain storage
                                    ~~~~~~~~~~~~~~~
  UPLC AST                         +---------------------------+
  (in memory)                      | CBOR bytestring wrapper   |
       |                           |  +----------------------+ |
       |--- Flat encode --->       |  | Flat-encoded bits     | |
       |                           |  | (the actual program)  | |
       |                           |  +----------------------+ |
                                   +---------------------------+

  Execution (when a transaction references the script):

  +---------------------------+       +----------------+       +-----------+
  | CBOR bytestring wrapper   | --->  | Flat decode    | --->  | CEK       |
  |                           |       | -> UPLC AST    |       | Machine   |
  +---------------------------+       +----------------+       | evaluate  |
                                                               +-----------+
                                                                    |
                                                              ExBudget (CPU, MEM)
                                                              or Error
```

---

## 2. The Language

### 2.1 Grammar

A UPLC **program** is a version number plus a single **term**:

```
Program  ::=  (program  version  Term)

             version = Major.Minor.Patch   (e.g. 1.1.0)
```

A **term** is one of 10 possible forms:

```
Term  ::=  x                           variable reference
        |  (con T c)                   constant value
        |  (builtin b)                builtin function
        |  (lam x M)                  lambda abstraction
        |  [M N]                      function application
        |  (delay M)                  suspend evaluation
        |  (force M)                  trigger suspended computation
        |  (constr k M1 ... Mm)       constructor (tag k, m fields)
        |  (case M N1 ... Nn)         case analysis (n branches)
        |  (error)                    abort execution
```

### 2.2 The 10 Term Types (visual)

```
+----------+     +----------+     +----------+     +----------+     +----------+
| Variable |     | Constant |     | Builtin  |     |  Lambda  |     |  Apply   |
|    x     |     | (con T c)|     |(builtin b)|    | (lam x M)|     |  [M N]   |
| look up  |     | literal  |     | primitive|     | create   |     | call fn  |
| in env   |     | value    |     | function |     | function |     | with arg |
+----------+     +----------+     +----------+     +----------+     +----------+

+----------+     +----------+     +----------+     +----------+     +----------+
|  Delay   |     |  Force   |     |  Constr  |     |   Case   |     |  Error   |
|(delay M) |     |(force M) |     |(constr k |     |(case M   |     | (error)  |
| suspend  |     | resume   |     |  M1..Mm) |     |  N1..Nn) |     | abort    |
| eval     |     | delayed  |     | build    |     | match on |     | now      |
+----------+     +----------+     +----------+     +----------+     +----------+
```

**C# representation** (`Types/Term.cs`):

```csharp
public abstract record Term<TBinder>;

public sealed record Var<TBinder>(TBinder Name) : Term<TBinder>;
public sealed record Lambda<TBinder>(TBinder Parameter, Term<TBinder> Body) : Term<TBinder>;
public sealed record Apply<TBinder>(Term<TBinder> Function, Term<TBinder> Argument) : Term<TBinder>;
public sealed record Const<TBinder>(Constant Value) : Term<TBinder>;
public sealed record Builtin<TBinder>(DefaultFunction Function) : Term<TBinder>;
public sealed record Delay<TBinder>(Term<TBinder> Body) : Term<TBinder>;
public sealed record Force<TBinder>(Term<TBinder> Body) : Term<TBinder>;
public sealed record Constr<TBinder>(int Tag, ImmutableArray<Term<TBinder>> Fields) : Term<TBinder>;
public sealed record Case<TBinder>(Term<TBinder> Scrutinee, ImmutableArray<Term<TBinder>> Branches) : Term<TBinder>;
public sealed record Error<TBinder> : Term<TBinder>;
```

The `TBinder` parameter allows the same AST to represent both:
- **Named programs** (`Program<Name>`) -- parsed from text, used during development
- **DeBruijn programs** (`Program<DeBruijn>`) -- serialized form, used on-chain

### 2.3 Built-in Types

| Type | C# Type | Description |
|------|---------|-------------|
| `integer` | `BigInteger` | Arbitrary-precision signed integer |
| `bytestring` | `byte[]` | Sequence of bytes |
| `string` | `string` | Unicode string |
| `bool` | `bool` | `True` or `False` |
| `unit` | (singleton) | The unit value `()` |
| `data` | `PlutusData` | Recursive structured data (see below) |
| `list(t)` | `ImmutableList<T>` | Homogeneous list |
| `pair(a,b)` | `(A, B)` | Pair of values |
| `array(t)` | `ImmutableArray<T>` | Indexed array (Batch 6) |
| `bls12_381_G1_element` | BLS G1 point | 48-byte compressed curve point |
| `bls12_381_G2_element` | BLS G2 point | 96-byte compressed curve point |
| `bls12_381_mlresult` | BLS ML result | Opaque pairing result |

### 2.4 PlutusData

`PlutusData` (called `data` in the spec) is a **5-variant recursive algebraic type** used to pass
structured arguments to scripts. It is the universal data interchange format on Cardano.

```
data Data =
    Constr  Integer [Data]          -- tagged constructor with fields
  | Map     [(Data, Data)]          -- key-value pairs
  | List    [Data]                  -- list of data items
  | I       Integer                 -- integer
  | B       ByteString              -- bytestring
```

Visual structure:

```
                        PlutusData
                            |
         +----------+------+------+----------+----------+
         |          |             |          |          |
      Constr       Map          List       Integer  ByteString
      tag: int     pairs:       items:      value:    value:
      fields:      [(D,D)]      [D]         BigInt    byte[]
      [Data]

Example: Constr 1 [I 42, B #deadbeef, List [I 1, I 2, I 3]]

         Constr(1)
        /    |    \
    I(42)  B(#dead  List
            beef)    |
                  [I(1), I(2), I(3)]
```

**C# representation** (`Types/PlutusData.cs`):

```csharp
public abstract record PlutusData;
public sealed record ConstrData(BigInteger Tag, IReadOnlyList<PlutusData> Fields) : PlutusData;
public sealed record MapData(IReadOnlyList<(PlutusData Key, PlutusData Value)> Pairs) : PlutusData;
public sealed record ListData(IReadOnlyList<PlutusData> Items) : PlutusData;
public sealed record IntegerData(BigInteger Value) : PlutusData;
public sealed record ByteStringData(byte[] Value) : PlutusData;
```

### 2.5 DeBruijn Indices

In serialized UPLC programs, variables are represented by **DeBruijn indices** rather than names.
A DeBruijn index is a **1-based** natural number that counts how many lambda binders you must
cross (going outward) to reach the binding site.

```
Named form:       (lam x (lam y [x y]))

DeBruijn form:    (lam _ (lam _ [2 1]))
                                  ^ ^
                                  | +--- index 1 = nearest lambda (y)
                                  +----- index 2 = next outer lambda (x)

Example walkthrough:

    (lam _                      <-- binding depth 1
        (lam _                  <-- binding depth 2
            [
                2               <-- "go out 2 lambdas" = first lam (was 'x')
                1               <-- "go out 1 lambda"  = second lam (was 'y')
            ]
        )
    )
```

Why DeBruijn indices?
- **No alpha-equivalence issues** -- structurally identical programs are literally identical
- **Smaller serialization** -- no variable name strings needed
- **Simpler substitution** -- environment lookup by index, O(n) with linked list

**C# representation** (`Types/Binder.cs`):

```csharp
// For parsed text programs (human-readable)
public sealed record Name(string Text, int Unique);

// For serialized programs (on-chain)
public sealed record DeBruijn(int Index);  // 1-based
```

In the serialized (Flat) format, the bound variable in `(lam 0 M)` always uses index 0
(it is discarded during deserialization). Free variables use indices > 0.

---

## 3. The CEK Machine

The CEK machine is an abstract machine that efficiently evaluates UPLC terms. The name comes
from its three core components: **C**ontrol (the term being evaluated), **E**nvironment (variable
bindings), and **K**ontinuation (the stack of pending operations).

### 3.1 States

The machine has 4 possible states:

```
+---------------------------------------------------+
|                  CEK Machine States                 |
+---------------------------------------------------+
|                                                     |
|   +-------------+          +-------------+          |
|   |  Compute    |          |  Return     |          |
|   |   s;p > M   |  -----  |   s < V     |          |
|   | "evaluate   | \      /| "got a value,|         |
|   |  term M in  |  \    / |  pop frame   |          |
|   |  env p,     |   \  /  |  and decide  |          |
|   |  stack s"   |    \/   |  what to do" |          |
|   +------+------+   /\   +------+-------+          |
|          |          /  \         |                   |
|          v         /    \        v                   |
|   +------+------+ /      \ +----+-------+           |
|   |   Error     |/        \|   Halt     |           |
|   |     <>      |          |    [] V    |           |
|   | "evaluation |          | "stack is  |           |
|   |  failed"    |          |  empty,    |           |
|   +-------------+          |  done!"    |           |
|                             +------------+           |
+---------------------------------------------------+

Legend:
  s;p > M   =  Compute: evaluate term M in environment p, with stack s
  s < V     =  Return:  return value V to the continuation on stack s
  <>        =  Error:   evaluation has failed
  [] V      =  Halt:    stack is empty, V is the final result
```

### 3.2 The Compute Phase (>)

When in the Compute state `s; p > M`, the machine inspects the term `M`:

```
COMPUTE: s; p > M
====================

  M = x (variable)
  +--> look up x in environment p
       s < p[x]                          (return the value)

  M = (con T c)
  +--> s < <con T c>                     (constants are already values)

  M = (lam x M')
  +--> s < <lam x M' p>                  (create a CLOSURE capturing env p)

  M = (delay M')
  +--> s < <delay M' p>                  (create a suspended computation)

  M = (builtin b)
  +--> s < <builtin b [] [] eta(b)>      (partially applied builtin, 0 args)

  M = [M N]    (application)
  +--> [_ (N, p)] . s; p > M            (push "await function" frame,
                                          evaluate the function M first)

  M = (force M')
  +--> (force _) . s; p > M'            (push force frame, evaluate M')

  M = (constr k M1 ... Mm)
  +--> if m = 0: s < <constr k []>       (no fields, immediate value)
       if m > 0: (constr k [] _ (M2..Mm, p)) . s; p > M1
                                          (push constr frame, eval first field)

  M = (case M' N1 ... Nn)
  +--> (case _ (N1..Nn, p)) . s; p > M' (push case frame, eval scrutinee)

  M = (error)
  +--> <>                                (transition to Error state)
```

### 3.3 The Return Phase (<)

When in the Return state `s < V`, the machine pops a frame from the stack:

```
RETURN: s < V
==============

  Stack = []  (empty)
  +--> [] V                              (HALT -- we are done!)

  Frame = [_ (M, p)]                    (awaiting function: we evaluated the
  +--> [V _] . s; p > M                  function, now evaluate the argument)

  Frame = [<lam x M p> _]               (awaiting argument for a lambda)
  +--> s; p[x -> V] > M                  (bind arg, evaluate body -- the "beta
                                          reduction" step)

  Frame = [_ V']                         (awaiting argument for non-lambda)
  +--- V' must be <builtin ...>
       +--> see builtin handling below

  Frame = (force _)
  +--- V = <delay M p>
       +--> s; p > M                     (unwrap delay, evaluate body)
  +--- V = <builtin b args [] (i.eta)>   (force a polymorphic builtin)
       +--> consume one force count

  Frame = (constr k Vs _ (Ms, p))        (accumulating constructor fields)
  +--- Ms is non-empty: push next field, keep evaluating
  +--- Ms is empty: s < <constr k (Vs ++ [V])>   (all fields done)

  Frame = (case _ (Branches, p))
  +--- V = <constr k V1 ... Vm>
       +--> select branch k+1, apply it to the fields V1..Vm
            by pushing [_ V1] [_ V2] ... frames
```

### 3.4 Environment

The environment is a **linked list** mapping DeBruijn indices to values. When entering a lambda
body, a new binding is prepended. Lookup walks the list.

```
Environment (linked list):

  [] = empty environment

  p[x -> V] = extend environment p with binding x -> V
             = V :: p    (prepend)

  Lookup by DeBruijn index:
    p[1] = head of the list (most recent binding)
    p[2] = second element
    p[n] = n-th element

  Example:
    (lam _ (lam _ (lam _ [3 2 1])))

    After entering all 3 lambdas with args A, B, C:
    env = [C, B, A]     (C is most recent)

    Index 1 -> C   (head)
    Index 2 -> B
    Index 3 -> A   (tail)
```

**C# representation** (`Cek/Environment.cs`):

```csharp
// Immutable linked list -- O(1) extend, O(n) lookup
public sealed record Env(CekValue Head, Env? Tail)
{
    public static readonly Env Empty = ...;

    public CekValue Lookup(int deBruijnIndex)
    {
        Env current = this;
        for (int i = 1; i < deBruijnIndex; i++)
            current = current.Tail!;
        return current.Head;
    }
}
```

### 3.5 Continuation Frames

The stack is a list of **continuation frames** -- each frame records what operation is
waiting for a value. There are 7 frame types:

```
+--------------------+--------------------------------------------------+
| Frame Type         | Purpose                                          |
+--------------------+--------------------------------------------------+
| (no frame)         | Empty stack -- machine halts when returning to   |
|                    | this                                             |
+--------------------+--------------------------------------------------+
| [_ (M, p)]        | FrameAwaitFunTerm: we are evaluating the         |
|                    | function; M is the argument term (not yet         |
|                    | evaluated), p is its environment                  |
+--------------------+--------------------------------------------------+
| [_ V]             | FrameAwaitFunValue: we are evaluating the        |
|                    | function; V is the already-evaluated argument     |
+--------------------+--------------------------------------------------+
| [V _]             | FrameAwaitArg: we evaluated the function (V),    |
|                    | now waiting for the argument                      |
+--------------------+--------------------------------------------------+
| (force _)          | FrameForce: waiting for a delayed/builtin value  |
|                    | to force                                          |
+--------------------+--------------------------------------------------+
| (constr k Vs       | FrameConstr: building a constructor; Vs are      |
|   _ (Ms, p))       | already-evaluated fields, Ms are remaining        |
|                    | unevaluated fields                                |
+--------------------+--------------------------------------------------+
| (case _ (Ms, p))   | FrameCases: evaluating scrutinee; Ms are the     |
|                    | branch terms                                      |
+--------------------+--------------------------------------------------+
```

### 3.6 CEK Values

Runtime values in the CEK machine differ from terms -- they carry captured environments:

```
CEK Value  ::=  <con T c>                     -- constant (same as term)
            |   <lam x M p>                   -- closure: lambda + environment
            |   <delay M p>                   -- suspended computation + environment
            |   <constr k V1 ... Vn>          -- fully evaluated constructor
            |   <builtin b V1..Vk eta>        -- partially applied builtin
```

**C# representation** (`Cek/CekValue.cs`):

```csharp
public abstract record CekValue;
public sealed record VConstant(Constant Value) : CekValue;
public sealed record VLambda(DeBruijn Parameter, Term<DeBruijn> Body, Env Env) : CekValue;
public sealed record VDelay(Term<DeBruijn> Body, Env Env) : CekValue;
public sealed record VConstr(int Tag, ImmutableArray<CekValue> Fields) : CekValue;
public sealed record VBuiltin(DefaultFunction Function, ImmutableArray<CekValue> Args,
                              ImmutableArray<CekValue> ForceArgs, ExpectedArgs Expected) : CekValue;
```

### 3.7 Discharging (Value -> Term Conversion)

When the machine halts with a value, we need to convert it back to a UPLC term. This process
is called **discharging** (spec section 2.4.1). It substitutes environment bindings back into terms:

```
Discharge(<con T c>)           = (con T c)
Discharge(<delay M p>)         = (delay M) @p          -- substitute env into body
Discharge(<lam x M p>)         = (lam x M) @p          -- substitute env into body
Discharge(<constr k V1..Vn>)   = (constr k Discharge(V1) ... Discharge(Vn))
Discharge(<builtin b V1..Vk>)  = [... [[(builtin b)] Discharge(V1)] ... Discharge(Vk)]

Where M @p means: for each binding (x -> V) in env p,
  substitute Discharge(V) for x in M (rightmost/most-recent first)
```

### 3.8 Worked Example

Evaluating `[(lam _ (lam _ [2 1])) (con integer 10) (con integer 20)]`:

This is actually `[[(lam _ (lam _ [2 1])) (con integer 10)] (con integer 20)]`.

```
Step 1: Compute  []; [] > [[(lam _ (lam _ [2 1])) (con int 10)] (con int 20)]
  --> push frame [_ ((con int 20), [])], compute inner apply

Step 2: Compute  [_ ((con int 20), [])]; [] > [(lam _ (lam _ [2 1])) (con int 10)]
  --> push frame [_ ((con int 10), [])], compute function

Step 3: Compute  [_ ((con int 10), [])], [_ ((con int 20), [])]; [] > (lam _ (lam _ [2 1]))
  --> return closure <lam _ (lam _ [2 1]) []>

Step 4: Return   [_ ((con int 10), [])], [_ ((con int 20), [])] < <lam _ (lam _ [2 1]) []>
  --> frame is FrameAwaitFunTerm, push [<lam ...> _], compute arg

Step 5: Compute  [<lam _ (lam _ [2 1]) []> _], [_ ((con int 20), [])]; [] > (con int 10)
  --> return <con int 10>

Step 6: Return   [<lam _ (lam _ [2 1]) []> _], [_ ((con int 20), [])] < <con int 10>
  --> frame is FrameAwaitArg with lambda, beta-reduce:
      env = [<con int 10>], compute body

Step 7: Compute  [_ ((con int 20), [])]; [<con int 10>] > (lam _ [2 1])
  --> return closure <lam _ [2 1] [<con int 10>]>

Step 8: Return   [_ ((con int 20), [])] < <lam _ [2 1] [<con int 10>]>
  --> FrameAwaitFunTerm, push [<lam ...> _], compute arg

Step 9: Compute  [<lam _ [2 1] [<con int 10>]> _]; [] > (con int 20)
  --> return <con int 20>

Step 10: Return  [<lam _ [2 1] [<con int 10>]> _] < <con int 20>
  --> FrameAwaitArg with lambda, beta-reduce:
      env = [<con int 20>, <con int 10>], compute body [2 1]

Step 11: Compute []; [<con int 20>, <con int 10>] > [2 1]
  --> push [_ (1, env)], compute variable 2

Step 12: Compute [_ (1, env)]; env > 2
  --> lookup index 2 in env = <con int 10>
  --> return <con int 10>

Step 13: Return  [_ (1, env)] < <con int 10>
  --> FrameAwaitFunTerm, but <con int 10> is a constant, not a function!
  --> ERROR (cannot apply a constant)
```

(This example intentionally shows an error -- `[2 1]` tries to apply integer 10 to integer 20.)

---

## 4. Built-in Functions

UPLC provides **94 built-in functions** (tags 0-93) organized into 6 batches, introduced
across different Cardano hard forks.

### 4.1 Overview by Category

```
+------------------------------------------------------------------+
|                     BUILT-IN FUNCTIONS (94)                        |
+------------------------------------------------------------------+
|                                                                    |
| ARITHMETIC (10)        BYTESTRING (8)        STRING (4)           |
| addInteger         0   appendByteString  10   appendString     22 |
| subtractInteger    1   consByteString    11   equalsString     23 |
| multiplyInteger    2   sliceByteString   12   encodeUtf8       24 |
| divideInteger      3   lengthOfByteStr   13   decodeUtf8       25 |
| quotientInteger    4   indexByteString   14                       |
| remainderInteger   5   equalsByteString  15                       |
| modInteger         6   lessThanByteStr   16                       |
| equalsInteger      7   lessThanEqByteS   17                       |
| lessThanInteger    8                                               |
| lessThanEqualsInt  9                                               |
+------------------------------------------------------------------+
| CRYPTO (9)                              CONTROL FLOW (5)          |
| sha2_256              18                ifThenElse         26     |
| sha3_256              19                chooseUnit         27     |
| blake2b_256           20                trace              28     |
| verifyEd25519Sig      21                fstPair            29     |
| verifyEcdsaSecp...    52                sndPair            30     |
| verifySchnorrSecp...  53                                          |
| blake2b_224           72                LIST (6)                  |
| keccak_256            71                chooseList         31     |
| ripemd_160            86                mkCons             32     |
|                                         headList           33     |
| DATA (14)                               tailList           34     |
| chooseData         36                   nullList           35     |
| constrData         37                   dropList           88     |
| mapData            38                                             |
| listData           39                   PAIR/NIL (3)              |
| iData              40                   mkPairData         48     |
| bData              41                   mkNilData          49     |
| unConstrData       42                   mkNilPairData      50     |
| unMapData          43                                             |
| unListData         44                   SERIALIZATION (1)         |
| unIData            45                   serialiseData      51     |
| unBData            46                                             |
| equalsData         47                                             |
+------------------------------------------------------------------+
| BLS12-381 (19)                          BITWISE (12)              |
| bls12_381_G1_add      54               andByteString      75     |
| bls12_381_G1_neg      55               orByteString       76     |
| bls12_381_G1_scalarMul 56              xorByteString      77     |
| bls12_381_G1_equal    57               complementByteStr  78     |
| bls12_381_G1_hashTo.. 58               readBit            79     |
| bls12_381_G1_compress 59               writeBits          80     |
| bls12_381_G1_uncompr  60               replicateByte      81     |
| bls12_381_G2_add      61               shiftByteString    82     |
| bls12_381_G2_neg      62               rotateByteString   83     |
| bls12_381_G2_scalarMul 63              countSetBits       84     |
| bls12_381_G2_equal    64               findFirstSetBit    85     |
| bls12_381_G2_hashTo.. 65               ripemd_160         86     |
| bls12_381_G2_compress 66                                          |
| bls12_381_G2_uncompr  67               CONVERSION (3)             |
| bls12_381_millerLoop  68               integerToByteStr   73     |
| bls12_381_mulMlResult 69               byteStringToInt    74     |
| bls12_381_finalVerify 70               expModInteger      87     |
| bls12_381_G1_multiSca 92                                          |
| bls12_381_G2_multiSca 93               ARRAY (3)                  |
|                                         dropList           88     |
|                                         lengthOfArray      89     |
|                                         listToArray        90     |
|                                         indexArray          91     |
+------------------------------------------------------------------+
```

### 4.2 Batches and Ledger Language Mapping

| Batch | Builtins (tags) | Ledger Language | Protocol Version | Hard Fork |
|-------|----------------|-----------------|------------------|-----------|
| 1 | 0-50 (51 functions) | PlutusV1 | 5.0 | Alonzo (Sep 2021) |
| 2 | 51 (serialiseData) | PlutusV2 | 7.0 | Vasil (Jun 2022) |
| 3 | 52-53 (secp256k1) | PlutusV2 | 8.0 | Valentine (Feb 2023) |
| 4 | 54-74 (BLS, hashes, conversions) | PlutusV3 | 9.0 | Chang (Sep 2024) |
| 5 | 75-86 (bitwise, RIPEMD-160) | PlutusV3 | 10.0 | Plomin (Jan 2025) |
| 6 | 87-93 (arrays, expMod, multiScalarMul, dropList) | PlutusV3+ | -- | (upcoming) |

### 4.3 Force Counts (Polymorphic Builtins)

Some builtins are **polymorphic** -- they work on multiple types. In UPLC, polymorphism is
handled through `force`/`delay`. A polymorphic builtin must be `force`d before it can accept
arguments. The number of required forces depends on how many type parameters the function has.

```
Force count = 0 (monomorphic -- no force needed):
  addInteger, subtractInteger, multiplyInteger, divideInteger, ...
  sha2_256, sha3_256, blake2b_256, equalsData, serialiseData, ...

  Usage:  [[(builtin addInteger) (con integer 3)] (con integer 4)]

Force count = 1 (one type parameter):
  mkCons, headList, tailList, nullList, chooseList, chooseData, trace,
  fstPair, sndPair, chooseUnit, ifThenElse, ...

  Usage:  [[(force (builtin ifThenElse)) (con bool True)]
            (con integer 1)]
            (con integer 2)]

Force count = 2 (two type parameters):
  fstPair, sndPair

  Usage:  [(force (force (builtin fstPair)))
            (con (pair integer string) (42, "hello"))]
```

### 4.4 Partial Application

Builtins are **partially applied** -- they collect arguments one at a time until they have enough
(are "saturated"), then execute:

```
(builtin addInteger)                          -- 0 args, needs 2
  |
  |-- apply (con integer 3) -->
  |
<builtin addInteger [3] eta=[integer]>        -- 1 arg, needs 1 more
  |
  |-- apply (con integer 4) -->
  |
SATURATED! Execute: 3 + 4 = 7                -- return <con integer 7>
```

In the CEK machine, a `VBuiltin` value tracks:
1. Which function it is (`DefaultFunction`)
2. Arguments collected so far
3. Expected remaining argument types (`eta`)

When the last argument arrives, the machine calls the actual implementation function.

### 4.5 Selected Builtin Details

**Integer division** -- four flavors with different rounding:

```
            div/mod (round toward -infinity)      quot/rem (round toward zero)
a=7, b=2:   div=3, mod=1                          quot=3, rem=1
a=-7, b=2:  div=-4, mod=1                         quot=-3, rem=-1
a=7, b=-2:  div=-4, mod=-1                        quot=-3, rem=1
a=-7,b=-2:  div=3, mod=-1                         quot=3, rem=-1

Rule: div(a,b) * b + mod(a,b) = a
      quot(a,b) * b + rem(a,b) = a
```

**chooseData** -- a 6-argument function that pattern-matches on PlutusData:

```csharp
// chooseData(d, onConstr, onMap, onList, onInt, onByteString)
// Returns the branch corresponding to the variant of d
d match {
    ConstrData _     => onConstr,
    MapData _        => onMap,
    ListData _       => onList,
    IntegerData _    => onInt,
    ByteStringData _ => onByteString
}
```

**Signature verification** -- uniform 3-argument interface:

```
verifyEd25519Signature(vk: 32 bytes, message: any, sig: 64 bytes) -> bool
verifyEcdsaSecp256k1Signature(vk: 33 bytes, msgHash: 32 bytes, sig: 64 bytes) -> bool
verifySchnorrSecp256k1Signature(vk: 32 bytes, message: any, sig: 64 bytes) -> bool
```

---

## 5. Flat Serialization

UPLC programs are serialized using the **Flat** binary format -- a compact bit-level encoding
that is ~35% smaller than CBOR. This is the definitive on-chain representation.

### 5.1 Overall Structure

```
+---------------------------------------------------------------+
|                    Flat-encoded Program                         |
+---------------------------------------------------------------+
| Version (3 naturals)  |  Term (recursive)  |  Padding          |
| major | minor | patch |  4-bit tag + data  |  0*1 to byte      |
+---------------------------------------------------------------+

Example: (program 1.1.0 (con integer 42))

  Bits:
  [00000001]  version major = 1 (natural encoding)
  [00000001]  version minor = 1
  [00000000]  version patch = 0
  [0100]      term tag 4 = constant
  [1]         start of type tag list
  [0000]      type tag 0 = integer
  [0]         end of type tag list
  [10101000]  zigzag(42)=84, 7-bit chunks: [0000001][0010100]
  [00000010]  last chunk
  [000001]    padding (fill to byte boundary: 0s then a 1)
```

### 5.2 Term Tags (4-bit)

```
+----------+--------+---------+---------------------------------------------+
| Term     | Binary | Decimal | Subterm encoding                            |
+----------+--------+---------+---------------------------------------------+
| Variable | 0000   |    0    | natural number (DeBruijn index)             |
| Delay    | 0001   |    1    | term                                        |
| Lambda   | 0010   |    2    | (bound var index discarded) + term          |
| Apply    | 0011   |    3    | term + term                                 |
| Constant | 0100   |    4    | type tag list + value                       |
| Force    | 0101   |    5    | term                                        |
| Error    | 0110   |    6    | (nothing)                                   |
| Builtin  | 0111   |    7    | 7-bit function tag                          |
| Constr   | 1000   |    8    | natural (tag) + term list                   |
| Case     | 1001   |    9    | term + term list                            |
+----------+--------+---------+---------------------------------------------+

Note: Tags 1010-1111 (10-15) are reserved for future use.
```

### 5.3 Integer Encoding (Zigzag + Variable-length)

Integers are encoded in two steps:

**Step 1: Zigzag encoding** -- maps signed integers to natural numbers:

```
 0 -> 0
-1 -> 1
 1 -> 2
-2 -> 3
 2 -> 4
...

Formula:  n >= 0  -->  2n
          n < 0   -->  -2n - 1
```

**Step 2: Variable-length natural** -- split into 7-bit blocks, MSB-first continuation bit:

```
Each 7-bit block is prefixed by a continuation bit:
  1 = more blocks follow
  0 = this is the last block

Blocks are emitted least-significant first.

Example: encode integer 54321

  Zigzag: 54321 * 2 = 108642

  108642 in binary: 0000001 1010000 1100010

  Emit as 7-bit blocks (least significant first):
    1 1100010    (continuation=1, value=98)
    1 1010000    (continuation=1, value=80)
    0 0000001    (continuation=0, value=1, LAST)

  Bit stream: 1_1100010  1_1010000  0_0000110
              ^^^^^^^^^ ^^^^^^^^^ ^^^^^^^^^
              chunk 0    chunk 1    chunk 2 (final)
```

### 5.4 ByteString Encoding

ByteStrings use a chunked format with byte-alignment:

```
Before the bytestring content:
  1. Pad current bit position to next byte boundary (pad with 0s + final 1)

Then emit chunks:
  2. For each chunk (up to 255 bytes):
     - 1 byte: chunk length (1-255)
     - N bytes: chunk data
  3. Terminator: 1 byte with value 0 (zero-length chunk)

+--------+---------+------+--------+---------+------+--------+
| padding| len=255 | 255  | len=7  |  7      | len=0|
| 0..01  | (1 byte)| bytes| (1byte)| bytes   |(term)|
+--------+---------+------+--------+---------+------+--------+
          chunk 1          chunk 2            end

For empty bytestring: just pad + 0x00 (zero-length chunk immediately)
```

### 5.5 Type Tags (4-bit, for constants)

Constants are encoded as a type tag list followed by the value. The type tag list uses a
1-prefixed list encoding (1 = next tag, 0 = end of list):

```
+-------------------------+--------+---------+
| Type                    | Binary | Decimal |
+-------------------------+--------+---------+
| integer                 | 0000   |    0    |
| bytestring              | 0001   |    1    |
| string                  | 0010   |    2    |
| unit                    | 0011   |    3    |
| bool                    | 0100   |    4    |
| list                    | 0101   |    5    |
| pair                    | 0110   |    6    |
| (type application)      | 0111   |    7    |
| data                    | 1000   |    8    |
| bls12_381_G1_element    | 1001   |    9    |
| bls12_381_G2_element    | 1010   |   10    |
| bls12_381_MlResult      | 1011   |   11    |
| array                   | 1100   |   12    |
+-------------------------+--------+---------+

Compound types use type application (tag 7):
  list(integer)         =  [7, 5] . [0]     = "apply list to integer"
  pair(integer, string) =  [7, 7, 6] . [0] . [2]  = "apply (apply pair to int) to string"
  array(bool)           =  [7, 12] . [4]    = "apply array to bool"

Full encoding as a 1-prefixed list:
  (con integer 42)         type = 1_0000_0     "1 then integer(0) then 0(end)"
  (con list(integer) [...]) type = 1_0111_1_0101_1_0000_0
                                   "1, app(7), 1, list(5), 1, integer(0), 0(end)"
```

### 5.6 Builtin Tags (7-bit)

Built-in functions are encoded as 7-bit integers (0-93), using the fixed-width encoder E_7:

```
addInteger       = 0000000 (0)
subtractInteger  = 0000001 (1)
...
serialiseData    = 0110011 (51)
...
bls12_381_G1_add = 0110110 (54)
...
ripemd_160       = 1010110 (86)
expModInteger    = 1010111 (87)
...
bls12_381_G2_multiScalarMul = 1011101 (93)
```

### 5.7 Padding

At the end of a program, the bit stream is padded to a byte boundary:

```
Rule: append zero or more 0-bits, then a single 1-bit,
      such that the total length is a multiple of 8.

If already on a byte boundary: append full byte 00000001
If 1 bit past:                 append 0000001
If 2 bits past:                append 000001
...
If 7 bits past:                append 1

The 1-bit acts as a sentinel so the decoder knows where the padding starts.
```

### 5.8 Complete Worked Example (from spec)

```
Program: (program 5.0.2
           [[
             [(builtin indexByteString) (con bytestring #1a5f783625ee8c)]
             (con integer 54321)
           ]])

Bit-by-bit encoding:

  00000101   Final integer chunk: 0000101 = 5          \
  00000000   Final integer chunk: 0000000 = 0           > Version: 5.0.2
  00000010   Final integer chunk: 0000010 = 2          /
  0011       Term tag 3: apply                         \  outer [F A]
  0011       Term tag 3: apply                          > inner [F A]
  0111       Term tag 7: builtin                       |
  0001110    Builtin tag 14: indexByteString            |
  0100       Term tag 4: constant                      |
  1          Start of type tag list                    |
  0001       Type tag 1: bytestring                    |
  0          End of type tag list                      |
  001        Padding before bytestring                 |
  00000111   Bytestring chunk length: 7                |
  00011010   0x1a                                      |
  01011111   0x5f                                      |
  01111000   0x78                                      |
  00110110   0x36                                      |
  00100101   0x25                                      |
  11101110   0xee                                      |
  10001100   0x8c                                      |
  00000000   Bytestring chunk length: 0 (end)          /
  0100       Term tag 4: constant                      \
  1          Start of type tag list                     |
  0000       Type tag 0: integer                        > (con integer 54321)
  0          End of type tag list                      |
  11100010   Integer chunk (cont=1): 1100010 = 98      |  zigzag(54321) = 108642
  11010000   Integer chunk (cont=1): 1010000 = 80      |  108642 = 98 + 80*128
  00000110   Integer chunk (cont=0): 0000110 = 6       |          + 6*128*128
  000001     Padding                                   /
```

---

## 6. CBOR for PlutusData

PlutusData is encoded using CBOR (Concise Binary Object Representation) for on-chain storage
in transaction datums and redeemers, and for the `serialiseData` builtin. This is a **separate**
encoding from Flat -- Flat is for the script program, CBOR is for the data arguments.

### 6.1 CBOR Major Types

```
+------+---+------------------------------------------------+
| Type | # | Encoding                                       |
+------+---+------------------------------------------------+
| uint | 0 | Major type 0: non-negative integer              |
| nint | 1 | Major type 1: negative integer (encode -1-n)    |
| bstr | 2 | Major type 2: bytestring                        |
| tstr | 3 | Major type 3: text string                        |
| arr  | 4 | Major type 4: array (list)                       |
| map  | 5 | Major type 5: map (key-value pairs)              |
| tag  | 6 | Major type 6: semantic tag                       |
+------+---+------------------------------------------------+

Head encoding:  first byte = (major_type << 5) | argument

  argument 0-23:    value is argument itself          (1 byte head)
  argument 24:      value in next 1 byte              (2 byte head)
  argument 25:      value in next 2 bytes             (3 byte head)
  argument 26:      value in next 4 bytes             (5 byte head)
  argument 27:      value in next 8 bytes             (9 byte head)
  argument 31:      indefinite length                 (1 byte head)
```

### 6.2 PlutusData Encoding

```
Encoding rules for each PlutusData variant:

  Integer (I n):
  +---------------------------+----------------------------------------------+
  | Range                     | Encoding                                     |
  +---------------------------+----------------------------------------------+
  | 0 <= n < 2^64            | CBOR major type 0, value = n                 |
  | -2^64 <= n <= -1          | CBOR major type 1, value = -n - 1            |
  | n >= 2^64                 | CBOR tag 2 + bytestring (big-endian bytes)   |
  | n < -2^64                 | CBOR tag 3 + bytestring (big-endian of -n-1) |
  +---------------------------+----------------------------------------------+

  ByteString (B s):
    If len <= 64: single definite-length bytestring (major type 2)
    If len > 64:  indefinite-length bytestring, 64-byte chunks
                  (major type 2, argument 31, then chunks, then break 0xFF)

  List (List items):
    Indefinite-length array: 0x9F, then each item, then 0xFF (break)

  Map (Map pairs):
    Definite-length map: major type 5, length = number of pairs,
    then alternating key, value, key, value ...

  Constr (Constr tag fields):
    Tag encoding depends on the constructor tag value:
```

### 6.3 Constructor Tag Encoding

This is the most complex part of PlutusData CBOR encoding:

```
+-------------------+----------------------------------------------+
| Constructor tag i | CBOR encoding                                |
+-------------------+----------------------------------------------+
| 0 <= i <= 6       | CBOR tag (121 + i)                           |
|                   | followed by indefinite-length array of fields |
|                   | e.g., tag 0 -> CBOR tag 121                  |
|                   |      tag 6 -> CBOR tag 127                   |
+-------------------+----------------------------------------------+
| 7 <= i <= 127     | CBOR tag (1280 + i - 7)                      |
|                   | followed by indefinite-length array of fields |
|                   | e.g., tag 7  -> CBOR tag 1280                |
|                   |      tag 127 -> CBOR tag 1400                |
+-------------------+----------------------------------------------+
| i >= 128          | CBOR tag 102                                 |
|                   | followed by definite-length array [i, fields] |
|                   | where i is CBOR-encoded integer              |
|                   | and fields is indefinite-length array         |
+-------------------+----------------------------------------------+

Examples:

  Constr 0 [I 42]
  --> CBOR tag 121, indefinite array [42], break
  --> D8 79   9F   18 2A   FF
      ^^^^   ^^   ^^^^^   ^^
      tag    indef  42    break
      121    array

  Constr 1 []
  --> CBOR tag 122, indefinite array [], break
  --> D8 7A   9F   FF

  Constr 200 [B #CAFE]
  --> CBOR tag 102, definite array of 2:
      [200, indefinite array [#CAFE], break]
  --> D8 66   82   18 C8   9F   42 CA FE   FF
```

### 6.4 Comparison: Flat vs CBOR

```
                    Flat                            CBOR
Purpose:            Script program encoding         PlutusData encoding
Used for:           Script bodies on-chain          Datums, redeemers, serialiseData
Unit:               Bits                            Bytes
Self-describing:    No (needs schema)               Yes (major types in stream)
Size:               ~35% smaller than CBOR          Larger but self-describing
Integers:           Zigzag + 7-bit variable-length  Major type 0/1 + tags 2/3 for big
ByteStrings:        255-byte chunks, bit-padded     64-byte chunks (PlutusData encoding)
```

---

## 7. Cost Model and Budget Tracking

Every UPLC evaluation runs under a strict **budget** that limits both CPU time and memory
usage. This is essential for a blockchain -- it prevents denial-of-service attacks and
ensures all validators can predict execution cost.

### 7.1 ExBudget

```csharp
// The fundamental unit of cost tracking
public record struct ExBudget(long Cpu, long Mem);

// Budgets are added together during execution
ExBudget total = step1 + step2 + builtinCost;
```

### 7.2 Machine Step Costs

Every state transition in the CEK machine costs a fixed amount:

```
+------------------+------------------+------------------+
| Step Type        | CPU Cost         | Memory Cost      |
+------------------+------------------+------------------+
| Compute (>)      | 16,000           | 100              |
| Return  (<)      | 16,000           | 100              |
+------------------+------------------+------------------+
| Startup          | 100              | 100              |
+------------------+------------------+------------------+

These values come from the Cardano protocol parameters (cost model).
```

### 7.3 Builtin Costs

Each builtin function has its own cost model, defined as a **cost function** that takes the
sizes of the arguments and returns an ExBudget. The cost algebra includes:

```
Cost Function Types:
+-------------------+------------------------------------------+
| Model             | Formula                                  |
+-------------------+------------------------------------------+
| Constant          | c                                        |
| Linear (1 var)    | a * size + b                             |
| Quadratic (1 var) | a * size^2 + b * size + c                |
| Linear (2 vars)   | a * size1 + b * size2 + c                |
| Added sizes       | f(size1 + size2)                         |
| Subtracted sizes  | f(max(0, size1 - size2))                 |
| Multiplied sizes  | f(size1 * size2)                         |
| Min size          | f(min(size1, size2))                     |
| Max size          | f(max(size1, size2))                     |
| Literal size      | the integer value itself is the "size"   |
+-------------------+------------------------------------------+

Examples:
  addInteger CPU cost     = linear in max(size1, size2)
  multiplyInteger CPU cost = quadratic in sum(size1, size2)
  sha2_256 CPU cost       = linear in input length
  verifyEd25519 CPU cost  = constant (signature checks are constant-time)
```

**Size measurement** -- how "size" is determined for each type:

```
integer:     number of 64-bit words needed = ceil(ceil(log2(|n|+1)) / 64)
             (minimum 1 for the value 0)
bytestring:  length in bytes
string:      length in characters
bool:        1
unit:        1
list:        not directly measured (cost is per-element)
data:        size of the CBOR serialization in bytes
```

### 7.4 Slippage (Batched Budget Checking)

Checking the budget on every single step would be expensive. Instead, the machine uses
**slippage**: it accumulates unbudgeted steps and only checks the budget periodically.

```
+-----------------------------------------------------------------+
|                     Slippage Mechanism                            |
+-----------------------------------------------------------------+
|                                                                   |
|  unbudgetedSteps = 0                                              |
|                                                                   |
|  for each CEK step:                                               |
|    unbudgetedSteps++                                              |
|    if unbudgetedSteps >= 200:   <-- slippage threshold            |
|      spendBudget(unbudgetedSteps * stepCost)                      |
|      if budget < 0: ERROR "over budget"                           |
|      unbudgetedSteps = 0                                          |
|                                                                   |
|  (Builtin executions always flush immediately)                    |
+-----------------------------------------------------------------+

The slippage threshold of 200 means the machine can overshoot its budget
by at most 200 * stepCost before noticing. This is acceptable because:
  200 * 16000 CPU = 3,200,000 CPU (tiny fraction of a typical 10B budget)
```

### 7.5 Startup Cost

Before evaluation begins, a fixed startup cost is charged:

```
Startup cost: ExBudget(Cpu: 100, Mem: 100)
```

This accounts for the overhead of initializing the machine.

---

## 8. Ledger Languages

Cardano uses **ledger languages** to control which features are available to scripts.
Each ledger language freezes a specific set of builtins, language version, and semantics.

### 8.1 Overview

```
+--------------------------------------------------------------------+
|                     Cardano Plutus Timeline                          |
+--------------------------------------------------------------------+
|                                                                      |
|  Sep 2021        Jun 2022        Feb 2023     Sep 2024    Jan 2025  |
|  Alonzo          Vasil           Valentine    Chang       Plomin    |
|  PV 5.0          PV 7.0          PV 8.0       PV 9.0      PV 10.0  |
|    |               |               |            |            |       |
|    v               v               v            v            v       |
|  PlutusV1        PlutusV2                     PlutusV3              |
|  Batch 1         + Batch 2       + Batch 3    + Batch 4   + Batch 5|
|  51 builtins     + serialiseData + secp256k1  + BLS, hash + bitwise|
|  UPLC 1.0.0                                   UPLC 1.1.0           |
|                                                (+ constr/case)      |
+--------------------------------------------------------------------+
```

### 8.2 Detailed Comparison

```
+------------------+----------+----------+----------+
| Feature          | PlutusV1 | PlutusV2 | PlutusV3 |
+------------------+----------+----------+----------+
| Protocol version | 5.0      | 7.0      | 9.0      |
| UPLC version     | 1.0.0    | 1.0.0    | 1.1.0    |
| constr/case      | No       | No       | Yes      |
| Batch 1          | Yes      | Yes      | Yes      |
| Batch 2          | No       | Yes      | Yes      |
| Batch 3          | No       | Yes*     | Yes      |
| Batch 4          | No       | No       | Yes      |
| Batch 5          | No       | No       | Yes**    |
| Semantics        | Variant 1| Variant 1| Variant 2|
+------------------+----------+----------+----------+

*  Batch 3 added at PV 8.0 (Valentine), still under PlutusV2
** Batch 5 added at PV 10.0 (Plomin), still under PlutusV3

Semantics variant 2 changes: consByteString now fails on
  out-of-range first argument (0-255 required) instead of
  silently taking modulo 256.
```

### 8.3 Implications for Implementation

Each ledger language is essentially a configuration:

```csharp
// Pseudocode for evaluator configuration
record LedgerLanguageConfig(
    HashSet<DefaultFunction> AllowedBuiltins,
    Version MinVersion,
    Version MaxVersion,
    SemanticsVariant Semantics,
    CostModelParameters CostModel
);

// PlutusV1 config
new LedgerLanguageConfig(
    AllowedBuiltins: Batch1Functions,
    MinVersion: new(1, 0, 0),
    MaxVersion: new(1, 0, 0),
    Semantics: SemanticsVariant.V1,
    CostModel: alonzoCostModel
);
```

---

## 9. C# Implementation Notes

### 9.1 Architecture

```
src/Chrysalis.Plutus/
|
+-- Types/                      Core AST types
|   +-- Term.cs                 10-variant Term<TBinder> (abstract record + sealed subtypes)
|   +-- Constant.cs             12-variant Constant type
|   +-- ConstantType.cs         Recursive type tags for Flat encoding
|   +-- PlutusData.cs           5-variant PlutusData
|   +-- DefaultFunction.cs      enum with 94 builtin tags (0-93)
|   +-- ExBudget.cs             record struct ExBudget(long Cpu, long Mem)
|   +-- Program.cs              Program<TBinder> with Version
|   +-- Binder.cs               Name and DeBruijn binder types
|
+-- Flat/                       Flat binary codec
|   +-- BitReader.cs            Bit-level input (popBit, popBits, takeBytes)
|   +-- BitWriter.cs            Bit-level output (pushBit, pad)
|   +-- FlatDecoder.cs          bytes -> Program<DeBruijn>
|   +-- FlatEncoder.cs          Program<DeBruijn> -> bytes
|
+-- Cbor/                       Minimal CBOR codec (PlutusData only)
|   +-- CborWriter.cs           ~100 lines, encodes PlutusData to bytes
|   +-- CborReader.cs           ~100 lines, decodes bytes to PlutusData
|
+-- Text/                       Human-readable text format
|   +-- Lexer.cs                Tokenizer for UPLC text syntax
|   +-- Parser.cs               Recursive descent -> Program<Name>
|   +-- DeBruijnConverter.cs    Program<Name> -> Program<DeBruijn>
|   +-- PrettyPrinter.cs        Term -> readable text string
|
+-- Cek/                        CEK machine (evaluator)
|   +-- CekMachine.cs           Main loop: Compute/Return/Error/Halt
|   +-- CekValue.cs             Runtime values (5 variants)
|   +-- Environment.cs          Linked-list env for DeBruijn lookup
|   +-- Context.cs              7 continuation frame types
|   +-- Discharge.cs            Value -> Term conversion
|   +-- CostModel.cs            Cost algebra (linear, quadratic, etc.)
|   +-- ExMem.cs                Size measurement for costing
|   +-- MachineCosts.cs         Per-step costs
|   +-- DefaultCosts.cs         Protocol parameter cost tables
|
+-- Builtins/                   Built-in function implementations
|   +-- BuiltinDispatch.cs      Central dispatch table
|   +-- IntegerBuiltins.cs      add, subtract, multiply, divide, etc.
|   +-- ByteStringBuiltins.cs   append, cons, slice, index, etc.
|   +-- CryptoBuiltins.cs       SHA, Blake, Ed25519, secp256k1, BLS
|   +-- StringBuiltins.cs       append, equals, encode/decodeUtf8
|   +-- ControlBuiltins.cs      ifThenElse, chooseUnit/List/Data, trace
|   +-- ListBuiltins.cs         mkCons, head, tail, null, drop
|   +-- PairBuiltins.cs         fstPair, sndPair, mkPairData, mkNilData
|   +-- DataBuiltins.cs         constrData, mapData, un*, equalsData
|   +-- BlsBuiltins.cs          BLS12-381 curve operations
|   +-- BitwiseBuiltins.cs      and/or/xor, shift, rotate, read/writeBits
|   +-- ConversionBuiltins.cs   integer<->bytestring, expModInteger
|   +-- ArrayBuiltins.cs        lengthOfArray, listToArray, indexArray
|
+-- Evaluator.cs                High-level API entry point
+-- ScriptApplicator.cs         Apply parameters to parameterized scripts
```

### 9.2 Key Design Decisions

**Discriminated unions via abstract records**

C# does not have native discriminated unions (as of .NET 10). We simulate them using
an abstract record base with sealed subtypes. Pattern matching uses switch expressions:

```csharp
// Exhaustive matching on Term
CekValue result = term switch
{
    Var<DeBruijn> v       => env.Lookup(v.Name.Index),
    Const<DeBruijn> c     => new VConstant(c.Value),
    Lambda<DeBruijn> lam  => new VLambda(lam.Parameter, lam.Body, env),
    Delay<DeBruijn> d     => new VDelay(d.Body, env),
    Builtin<DeBruijn> b   => new VBuiltin(b.Function, ...),
    Apply<DeBruijn> app   => /* push frame, compute function */,
    Force<DeBruijn> f     => /* push force frame, compute inner */,
    Constr<DeBruijn> c    => /* push constr frame or immediate value */,
    Case<DeBruijn> c      => /* push case frame, compute scrutinee */,
    Error<DeBruijn>       => throw new EvaluationException(),
    _ => throw new InvalidOperationException()
};
```

**BigInteger for Plutus integers, long for budgets**

```csharp
// Plutus integers are arbitrary precision (spec requirement)
BigInteger plutusInt = BigInteger.Parse("123456789012345678901234567890");

// Budgets use long (matches Haskell Int64, sufficient for tracking)
ExBudget budget = new(Cpu: 10_000_000_000L, Mem: 14_000_000L);
```

**Linked-list environment for DeBruijn lookup**

```csharp
// Immutable, O(1) extend, O(n) lookup -- matches spec semantics exactly
// In practice, most lookups are shallow (index 1-3)
public sealed record Env(CekValue Head, Env? Tail)
{
    public Env Extend(CekValue value) => new(value, this);

    public CekValue Lookup(int index)
    {
        Env? current = this;
        for (int i = 1; i < index; i++)
            current = current!.Tail;
        return current!.Head;
    }
}
```

### 9.3 Dependencies

```
+-------------------------------------------+
|  Chrysalis.Plutus                          |
|  Target: .NET 10                           |
+-------------------------------------------+
|                                            |
|  BCL (System.*)                            |
|    SHA-256      System.Security.Cryptography.SHA256
|    SHA3-256     System.Security.Cryptography.SHA3_256
|    secp256k1    System.Security.Cryptography.ECDsa
|    BigInteger   System.Numerics.BigInteger
|                                            |
|  BouncyCastle.Cryptography (NuGet)         |
|    Ed25519      Org.BouncyCastle.Crypto.Signers.Ed25519Signer
|    Blake2b      Org.BouncyCastle.Crypto.Digests.Blake2bDigest
|    Keccak-256   Org.BouncyCastle.Crypto.Digests.KeccakDigest
|    RIPEMD-160   Org.BouncyCastle.Crypto.Digests.RipeMD160Digest
|    Schnorr      Org.BouncyCastle.Crypto (secp256k1 Schnorr)
|    BLS12-381    Org.BouncyCastle.Crypto (pairing operations)
|                                            |
|  NO native dependencies                    |
|  NO P/Invoke / FFI                         |
|  NO WASM                                   |
+-------------------------------------------+
```

### 9.4 Data Flow

```
                          Transaction Evaluation
                          =====================

  Input: transaction CBOR hex + UTxO set CBOR hex

  +----------------+     +------------------+     +------------------+
  | Parse tx CBOR  | --> | For each script  | --> | Flat-decode      |
  | Extract scripts|     | reference in tx: |     | script bytes     |
  | & redeemers    |     |                  |     | -> Program AST   |
  +----------------+     +------------------+     +--------+---------+
                                                           |
                                                           v
  +----------------+     +------------------+     +--------+---------+
  | Return         | <-- | Run CEK machine  | <-- | Apply arguments  |
  | EvaluationResult    | with budget from  |     | (datum, redeemer,|
  | (ExUnits or err)    | protocol params   |     |  script context)  |
  +----------------+     +------------------+     +------------------+
```

### 9.5 Quick Reference: Spec Section to C# File Mapping

| Spec Section | Topic | C# File |
|-------------|-------|---------|
| 2.1 (Fig 2.2) | Grammar / Term types | `Types/Term.cs` |
| 2.2 | Built-in types | `Types/Constant.cs`, `Types/ConstantType.cs` |
| 2.3 | Values, term reduction | `Cek/CekValue.cs` |
| 2.4 (Fig 2.10) | CEK machine transitions | `Cek/CekMachine.cs` |
| 2.4 (Fig 2.9) | CEK stack frames | `Cek/Context.cs` |
| 2.4.1 (Fig 2.11) | Discharging values | `Cek/Discharge.cs` |
| 2.5 | Cost accounting | `Cek/CostModel.cs`, `Cek/MachineCosts.cs` |
| 4.3.1 | Batch 1 builtins | `Builtins/*.cs` |
| 4.3.1.1 | PlutusData type | `Types/PlutusData.cs` |
| B.1-B.7 | CBOR for PlutusData | `Cbor/CborWriter.cs`, `Cbor/CborReader.cs` |
| C.1 | Flat padding | `Flat/BitWriter.cs`, `Flat/BitReader.cs` |
| C.2 | Basic Flat encodings | `Flat/BitWriter.cs`, `Flat/BitReader.cs` |
| C.3 | Flat for UPLC | `Flat/FlatDecoder.cs`, `Flat/FlatEncoder.cs` |
| C.4 | CBOR wrapping | `Evaluator.cs` |
