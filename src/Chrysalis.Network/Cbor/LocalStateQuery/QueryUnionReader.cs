using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

internal static class QueryUnionReader
{
    private const int Unscanned = -1;
    private const int NoFrame = -2;

    internal static IQueryReq Read(ReadOnlyMemory<byte> data, out int consumed) =>
        TryRead(data, 0, Unscanned, out IQueryReq? result, out consumed) ? result
        : throw CborError.Decode(data, 0, nameof(IQueryReq), "local state query array");

    private static bool TryRead(ReadOnlyMemory<byte> root, int offset, int end, [NotNullWhen(true)] out IQueryReq? result, out int consumed)
    {
        result = null;
        consumed = 0;
        ReadOnlyMemory<byte> data = root[offset..(end >= 0 ? end : root.Length)];
        ReadOnlySpan<byte> rest = data.Span;
        if (!TryArray(ref rest, out int count) || count == 0 || !NetworkUnionPrefix.TrySkipTag(ref rest)
            || !NetworkUnionPrefix.TryHead(ref rest, out byte initial, out ulong argument) || initial >> 5 > 1)
        {
            return false;
        }

        int prefix = data.Length - rest.Length;
        if (end == Unscanned && count > 1)
        {
            int frameLength = CborFrameScanner.GetFrameLength(data.Span);
            end = frameLength > 0 ? offset + frameLength : NoFrame;
            if (end >= 0)
            {
                data = root[offset..end];
                rest = data.Span[prefix..];
            }
        }

        bool framed = end >= 0;
        bool unsigned = initial >> 5 == 0;
        // BaseQuery has one optional field and precedes the list and global arms.
        // Scan at most once: definite 2-field wrappers pass end; indefinite levels inside a frame drop to NoFrame.
        if ((!framed || count <= 2) && argument <= int.MaxValue
            && TryChild(root, offset + prefix, count, framed ? (count == 2 ? end : NoFrame) : end, out IQueryReq? child, out int childLength))
        {
            consumed = prefix + childLength + (count < 0 && prefix + childLength < data.Length && data.Span[prefix + childLength] == 0xFF ? 1 : 0);
            if (!framed || consumed == data.Length)
            {
                int selector = unsigned ? (int)argument : -1 - (int)argument;
                result = new BaseQuery(selector, child) { Raw = data[..consumed], IsIndefinite = count < 0 };
                return true;
            }
            consumed = 0;
        }

        if (!unsigned)
        {
            return false;
        }
        if (count != 1 && (!framed || count <= 2)
            && (!framed || count > 0 || (rest.Length > 0 && rest[^1] == 0xFF))
            && TryCollection(framed && count < 0 ? rest[..^1] : rest, framed, out bool isAddressList))
        {
            result = isAddressList ? UtxoByAddressQuery.Read(data, out consumed) : UtxoByTxInQuery.Read(data, out consumed);
            return true;
        }

        if (framed && count > 1)
        {
            consumed = data.Length;
            result = new GlobalQuery(argument, data, data[prefix..], count - 1, false);
            return true;
        }

        result = GlobalQuery.FromPrefix(argument, data, prefix, count < 0, out consumed);
        if (framed && consumed != data.Length)
        {
            result = null;
            consumed = 0;
            return false;
        }
        return true;
    }

    private static bool TryChild(ReadOnlyMemory<byte> root, int offset, int count, int end, out IQueryReq? child, out int consumed)
    {
        child = null;
        consumed = 0;
        if (count == 1) { return true; }
        int limit = end >= 0 ? end : root.Length;
        if (offset < limit && root.Span[offset] is 0xF6 or 0xF7) { consumed = 1; return true; }
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw CborError.Decode(root, offset, nameof(IQueryReq), "query nesting within the available stack");
        }
        return TryRead(root, offset, end, out child, out consumed);
    }

    private static bool TryCollection(ReadOnlySpan<byte> data, bool requireExactEnd, out bool isAddressList)
    {
        isAddressList = true;
        if (!TryArray(ref data, out int count)) { return false; }
        if (count == 0) { return !requireExactEnd || data.IsEmpty; }
        if (count < 0 && (data.IsEmpty || data[0] == 0xFF))
        {
            if (!data.IsEmpty) { data = data[1..]; }
            return !requireExactEnd || data.IsEmpty; // Both list readers accept empty lists; address wins on X7.
        }
        ReadOnlySpan<byte> first = data;
        if (!NetworkUnionPrefix.TrySkipTag(ref first) || first.IsEmpty || first[0] >> 5 is not (2 or 4)) { return false; }
        isAddressList = first[0] >> 5 == 2;
        while (count > 0 || (count < 0 && !data.IsEmpty && data[0] != 0xFF))
        {
            if (!(isAddressList ? TryAddress(ref data) : TryTxIn(ref data, requireExactEnd))) { return false; }
            if (count > 0) { count--; }
        }
        if (count < 0 && !data.IsEmpty && data[0] == 0xFF) { data = data[1..]; }
        return !requireExactEnd || data.IsEmpty;
    }

    private static bool TryAddress(ref ReadOnlySpan<byte> data) =>
        NetworkUnionPrefix.TrySkipTag(ref data) && !data.IsEmpty && data[0] >> 5 == 2 && TrySkipField(ref data);

    private static bool TryTxIn(ref ReadOnlySpan<byte> data, bool requireExactEnd)
    {
        // TransactionInput.Read is lazy: require exact two-element arity in framed mode so extra items stay Global.
        if (!TryArray(ref data, out int count) || (requireExactEnd && count >= 0 && count != 2)
            || !TrySkipField(ref data) || !TrySkipField(ref data))
        {
            return false;
        }
        if (count < 0)
        {
            if (!data.IsEmpty && data[0] == 0xFF) { data = data[1..]; }
            else if (requireExactEnd) { return false; }
        }
        return true;
    }

    private static bool TrySkipField(ref ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) { return false; }
        if (data[0] == 0xFF) { data = data[1..]; return true; } // SAIB's lazy SkipDataItem consumes a root break.
        int length = CborFrameScanner.GetFrameLength(data);
        if (length > 0) { data = data[length..]; return true; }
        return length < 0 && TrySkipLegacySimpleValue(ref data);
    }

    private static bool TrySkipLegacySimpleValue(ref ReadOnlySpan<byte> data)
    {
        // Match SAIB's lazy SkipDataItem on legacy two-byte simple values (0xF8 < 32), root or nested.
        ReadOnlySpan<byte> rest = data;
        Stack<int> pending = new();
        pending.Push(1);
        while (pending.Count > 0)
        {
            int top = pending.Pop();
            if (rest.IsEmpty) { return false; }
            if (rest[0] == 0xFF)
            {
                if (top is not (-1 or -2)) { return false; }
                rest = rest[1..];
                continue;
            }
            if (top > 1) { pending.Push(top - 1); }
            else if (top < 0) { pending.Push(top == -1 ? -1 : top == -2 ? -3 : -2); }
            int major = rest[0] >> 5;
            if (major is 0 or 1 or 6)
            {
                if (!NetworkUnionPrefix.TryHead(ref rest, out _, out _)) { return false; }
                if (major == 6) { pending.Push(1); }
            }
            else if (major is 2 or 3)
            {
                int length = CborFrameScanner.GetFrameLength(rest);
                if (length <= 0) { return false; }
                rest = rest[length..];
            }
            else if (major is 4 or 5)
            {
                ulong mult = major == 5 ? 2UL : 1UL;
                if (rest[0] == (major == 4 ? 0x9F : 0xBF)) { rest = rest[1..]; pending.Push(major == 4 ? -1 : -2); }
                else if (NetworkUnionPrefix.TryHead(ref rest, out _, out ulong n) && n <= (ulong)rest.Length / mult)
                {
                    if (n != 0) { pending.Push((int)(n * mult)); }
                }
                else { return false; }
            }
            else
            {
                int info = rest[0] & 31;
                int width = info < 24 ? 1 : info <= 27 ? 1 + (1 << (info - 24)) : 0;
                if (width == 0 || rest.Length < width) { return false; }
                rest = rest[width..];
            }
        }
        data = rest;
        return true;
    }

    internal static bool TryArray(ref ReadOnlySpan<byte> data, out int count)
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
