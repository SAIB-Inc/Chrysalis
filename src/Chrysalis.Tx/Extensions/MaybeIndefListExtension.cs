using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Extensions;

public static class MaybeIndefListExtension
{
    public static List<T> Value<T>(this CborMaybeIndefList<T>? list)
    {
        return list switch
        {
            CborDefList<T> defList => defList.Value,
            CborIndefList<T> indefList => indefList.Value,
            CborDefListWithTag<T> defListWithTag => defListWithTag.Value,
            CborIndefListWithTag<T> indefListWithTag => indefListWithTag.Value,
            null => [],
            _ => throw new InvalidOperationException("Invalid List")
        };
    }
}