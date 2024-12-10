using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.Bytes, IsDefinite = true)]
public record CborBytes(byte[] Value) : RawCbor;

[CborSerializable(CborType.Bytes, IsDefinite = false, Size = 64)]
public record CborBoundedBytes(byte[] Value) : RawCbor;