using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborUnion]
public partial interface ICborMaybeIndefList<T> : ICborType;

[CborSerializable]
public readonly partial record struct CborDefList<T> : ICborMaybeIndefList<T>
{
    public partial List<T> Value { get; }
}

[CborSerializable]
public readonly partial record struct CborIndefList<T> : ICborMaybeIndefList<T>
{
    [CborIndefinite] public partial List<T> Value { get; }
}

[CborSerializable]
[CborTag(258)]
public readonly partial record struct CborDefListWithTag<T> : ICborMaybeIndefList<T>
{
    public partial List<T> Value { get; }
}

[CborSerializable]
[CborTag(258)]
public readonly partial record struct CborIndefListWithTag<T> : ICborMaybeIndefList<T>
{
    [CborIndefinite] public partial List<T> Value { get; }
}
