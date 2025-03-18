using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

// [CborSerializable]
[CborUnion]
public abstract partial record PlutusOption : CborBase<PlutusOption>
{
    // [CborSerializable]
    public partial record PlutusSome<T>([CborIndex(0)] T Value) : PlutusOption;

    // [CborSerializable]
    public partial record PlutusNone : PlutusOption;
}