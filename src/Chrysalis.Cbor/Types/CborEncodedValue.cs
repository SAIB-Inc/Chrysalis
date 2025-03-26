using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

[CborSerializable]
public partial record CborEncodedValue(byte[] Value) : CborBase;
