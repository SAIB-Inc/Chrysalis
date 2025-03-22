using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Primitives;

[CborSerializable]
public partial record PosixTime(ulong Value) : CborBase<PosixTime>;
