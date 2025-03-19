using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborSerializable]
[CborUnion]
public abstract partial record PlutusOption<T> : CborBase<PlutusOption<T>>
{
}

[CborSerializable]
public partial record PlutusSome<T>(T Value) : PlutusOption<T>;

[CborSerializable]
public partial record PlutusNone<T> : PlutusOption<T>;