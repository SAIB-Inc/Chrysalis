using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
public partial record CborBoundedBytes([CborSize(64)] byte[] Value) : CborBase;