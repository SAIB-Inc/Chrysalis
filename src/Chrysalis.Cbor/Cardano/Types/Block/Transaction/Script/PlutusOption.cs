using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

// [CborSerializable]
[CborUnion]
public abstract partial record PlutusOption<T> : CborBase<PlutusOption<T>>
{
    // [CborSerializable]
    public partial record PlutusSome<U>([CborIndex(0)] U Value) : PlutusOption<U>;

    // [CborSerializable]
    public partial record PlutusNone<U> : PlutusOption<U>;
}