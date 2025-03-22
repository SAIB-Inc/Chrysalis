using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Primitives;

[CborSerializable]
public partial record CborBoundedBytes([CborSize(64)] byte[] Value) : CborBase<CborBoundedBytes>;