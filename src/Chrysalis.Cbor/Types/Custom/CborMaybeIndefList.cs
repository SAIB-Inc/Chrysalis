using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Custom;

[CborSerializable]
[CborUnion]
public abstract partial record CborMaybeIndefList<T> : CborBase<CborMaybeIndefList<T>>
{
}

[CborSerializable]
public partial record CborDefList<T>(List<T> Value) : CborMaybeIndefList<T>;


[CborSerializable]
public partial record CborIndefList<T>(List<T> Value) : CborMaybeIndefList<T>;


[CborSerializable]
[CborTag(258)]
public partial record CborDefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T>;


[CborSerializable]
[CborTag(258)]
public partial record CborIndefListWithTag<T>(
    [CborIndefinite]
        List<T> Value
) : CborMaybeIndefList<T>;