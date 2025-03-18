using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Custom;

[CborSerializable]
public partial record ExactValue<T>(T Value) : CborBase<ExactValue<T>>;