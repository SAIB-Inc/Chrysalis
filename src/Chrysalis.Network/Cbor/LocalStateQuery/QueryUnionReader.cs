using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

internal static class QueryUnionReader
{
    internal static IQueryReq Read(ReadOnlyMemory<byte> data, out int consumed) =>
        TryRead(data, 0, out IQueryReq? result, out consumed) ? result
        : throw CborError.Decode(data, 0, nameof(IQueryReq), "local state query array");

    private static bool TryRead(ReadOnlyMemory<byte> root, int offset, [NotNullWhen(true)] out IQueryReq? result, out int consumed)
    {
        result = null;
        consumed = 0;
        ReadOnlyMemory<byte> data = root[offset..];
        ReadOnlySpan<byte> rest = data.Span;
        if (!TryArray(ref rest, out int count) || count == 0 || !NetworkUnionPrefix.TrySkipTag(ref rest)
            || !NetworkUnionPrefix.TryHead(ref rest, out byte initial, out ulong argument) || initial >> 5 > 1)
        {
            return false;
        }

        bool unsigned = initial >> 5 == 0;
        int prefix = data.Length - rest.Length;
        // BaseQuery has one optional field and precedes the list and global arms.
        // Decode the child at most once; a failed negative-selector child can leave a
        // successful GlobalQuery at an outer level. Do not over-read that prefix.
        if (argument <= int.MaxValue && TryChild(root, offset + prefix, count, out IQueryReq? child, out int childLength))
        {
            consumed = prefix + childLength;
            if (count < 0 && consumed < data.Length && data.Span[consumed] == 0xFF)
            {
                consumed++;
            }
            int selector = unsigned ? (int)argument : -1 - (int)argument;
            result = new BaseQuery(selector, child) { Raw = data[..consumed], IsIndefinite = count < 0 };
            return true;
        }

        if (!unsigned)
        {
            return false;
        }
        if (count != 1 && TryCollection(rest, out bool isAddressList))
        {
            result = isAddressList ? UtxoByAddressQuery.Read(data, out consumed) : UtxoByTxInQuery.Read(data, out consumed);
            return true;
        }

        // GlobalQuery historically reads only the selector (even for an array with
        // further items). The missing arguments are tracked as QR1, not fixed here.
        result = GlobalQuery.Read(data, out consumed);
        return true;
    }

    private static bool TryChild(ReadOnlyMemory<byte> root, int offset, int count, out IQueryReq? child, out int consumed)
    {
        child = null;
        consumed = 0;
        if (count == 1)
        {
            return true;
        }
        if (offset < root.Length && root.Span[offset] is 0xF6 or 0xF7)
        {
            consumed = 1;
            return true;
        }
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw CborError.Decode(root, offset, nameof(IQueryReq), "query nesting within the available stack");
        }
        return TryRead(root, offset, out child, out consumed);
    }

    private static bool TryCollection(ReadOnlySpan<byte> data, out bool isAddressList)
    {
        isAddressList = true;
        if (!TryArray(ref data, out int count))
        {
            return false;
        }
        if (count == 0 || (count < 0 && (data.IsEmpty || data[0] == 0xFF)))
        {
            return true; // Both list readers accept empty lists; address wins on X7.
        }
        ReadOnlySpan<byte> first = data;
        if (!NetworkUnionPrefix.TrySkipTag(ref first) || first.IsEmpty || first[0] >> 5 is not (2 or 4))
        {
            return false;
        }
        isAddressList = first[0] >> 5 == 2;
        while (count > 0 || (count < 0 && !data.IsEmpty && data[0] != 0xFF))
        {
            if (!(isAddressList ? TryAddress(ref data) : TryTxIn(ref data)))
            {
                return false;
            }
            if (count > 0)
            {
                count--;
            }
        }
        return true;
    }

    private static bool TryAddress(ref ReadOnlySpan<byte> data) =>
        NetworkUnionPrefix.TrySkipTag(ref data) && !data.IsEmpty && data[0] >> 5 == 2 && TrySkipField(ref data);

    private static bool TryTxIn(ref ReadOnlySpan<byte> data)
    {
        // TransactionInput.Read is lazy: it skips two CBOR fields without decoding
        // them or enforcing the declared count. Match those same boundaries here.
        if (!TryArray(ref data, out int count) || !TrySkipField(ref data) || !TrySkipField(ref data))
        {
            return false;
        }
        if (count < 0 && !data.IsEmpty && data[0] == 0xFF)
        {
            data = data[1..];
        }
        return true;
    }

    private static bool TrySkipField(ref ReadOnlySpan<byte> data)
    {
        // SAIB's lazy SkipDataItem also consumes a root break when called on one.
        int length = !data.IsEmpty && data[0] == 0xFF ? 1 : CborFrameScanner.GetFrameLength(data);
        if (length <= 0)
        {
            return false;
        }
        data = data[length..];
        return true;
    }

    private static bool TryArray(ref ReadOnlySpan<byte> data, out int count)
    {
        count = 0;
        if (!NetworkUnionPrefix.TrySkipTag(ref data) || data.IsEmpty || data[0] >> 5 != 4)
        {
            return false;
        }
        if (data[0] == 0x9F)
        {
            data = data[1..];
            count = -1;
            return true;
        }
        if (!NetworkUnionPrefix.TryHead(ref data, out _, out ulong argument) || argument > int.MaxValue)
        {
            return false;
        }
        count = (int)argument;
        return true;
    }
}
