using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Custom;

// [CborSerializable]
[CborUnion]
public abstract partial record CborMaybeIndefList<T> : CborBase<CborMaybeIndefList<T>>
{
    // [CborSerializable]
    public partial record CborDefList(List<T> Value) : CborMaybeIndefList<T>;


    // [CborSerializable]
    public partial record CborIndefList(List<T> Value) : CborMaybeIndefList<T>;


    // [CborSerializable]
    [CborTag(258)]
    public partial record CborDefListWithTag(List<T> Value) : CborMaybeIndefList<T>;


    // [CborSerializable]
    [CborTag(258)]
    public partial record CborIndefListWithTag(
        [CborIndefinite]
        List<T> Value
    ) : CborMaybeIndefList<T>;
}
