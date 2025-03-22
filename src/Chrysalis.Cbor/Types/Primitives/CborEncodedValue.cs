using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Primitives;

[CborSerializable]
public partial record CborEncodedValue(byte[] Value) : CborBase<CborEncodedValue>;
