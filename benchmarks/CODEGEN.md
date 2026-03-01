# Source Generator / Code Generation

Chrysalis uses a Roslyn **incremental source generator** (`Chrysalis.Cbor.CodeGen`) to emit serializer code at compile time. There is no separate codegen step — serializers are generated automatically during `dotnet build`.

## How It Works

1. Types annotated with `[CborSerializable]` (and optionally `[CborConstr]`, `[CborUnion]`, `[CborMap]`, etc.) are discovered by the source generator.
2. The generator emits `*.Serializer.g.cs` and `*.Metadata.g.cs` files containing `Read(ReadOnlyMemory<byte>)` and `Write(IBufferWriter<byte>, T)` methods.
3. These files are compiled as part of the project — no manual invocation needed.

## Viewing Generated Code

To dump the generated files to disk for inspection:

```bash
dotnet build src/Chrysalis.Cbor/Chrysalis.Cbor.csproj -p:EmitCompilerGeneratedFiles=true
```

Generated files appear under:
```
src/<Project>/obj/Debug/net10.0/generated/Chrysalis.Cbor.CodeGen/Chrysalis.Cbor.CodeGen.CborSerializerCodeGen/
```

For test project generated files:
```bash
dotnet build src/Chrysalis.Cbor.Test/Chrysalis.Test.csproj -p:EmitCompilerGeneratedFiles=true
```

## Forcing Regeneration

If generated code seems stale after modifying the source generator:

```bash
dotnet clean
dotnet build
```

## Key Files

- **Source generator entry point**: `src/Chrysalis.Cbor.CodeGen/CborSerializerCodeGen.cs`
- **Emitter (shared)**: `src/Chrysalis.Cbor.CodeGen/CborSerializerCodeGen.Emitter.cs`
- **Read emitter**: `src/Chrysalis.Cbor.CodeGen/CborSerializerCodeGen.ReadEmitter.cs`
- **Write emitter**: `src/Chrysalis.Cbor.CodeGen/CborSerializerCodeGen.WriteEmitter.cs`
- **Specialized emitters**: `src/Chrysalis.Cbor.CodeGen/Emitters/` (UnionEmitter, MapEmitter, ListEmitter, ConstructorEmitter, ContainerEmitter)

## CBOR Library

The project uses **Dahomey.Cbor** (not System.Formats.Cbor). Key differences:
- `CborReader` is a `ref struct` (stack-allocated, zero-overhead)
- `GetCurrentDataItemType()` auto-consumes semantic tags — always call `TryReadSemanticTag()` first if you need the tag value
- `ReadDataItem()` returns the full CBOR item including any semantic tag prefix
- `ReadByteString()` returns `ReadOnlySpan<byte>` — call `.ToArray()` when `byte[]` is needed
- `ReadBeginArray()` returns void; call `ReadSize()` separately (-1 = indefinite)
