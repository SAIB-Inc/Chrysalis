using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

// Inspect wire items with SAIB's generic reader, independently of all Cardano model types.
internal static class X8WireDifferences
{
    internal static string Classify(ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after)
    {
        RequireOneItem(before.Span);
        RequireOneItem(after.Span);
        SortedSet<string> classes = [];
        string? error = Compare(before, after, classes, "$", 0);
        return error is null ? string.Join(',', classes) : $"unexplained {error}";
    }

    private static void RequireOneItem(ReadOnlySpan<byte> bytes)
    {
        CborReader reader = new(bytes);
        _ = reader.ReadDataItem();
        if (!reader.Buffer.IsEmpty)
        {
            throw new FormatException("Expected exactly one CBOR item.");
        }
    }

    private static int Header(ReadOnlySpan<byte> item) => (item[0] & 31) switch
    {
        >= 24 and <= 27 and int additional => 1 + (1 << (additional - 24)),
        _ => 1
    };

    private static int PreferredHeader(ulong argument) => argument switch
    {
        < 24 => 1,
        <= byte.MaxValue => 2,
        <= ushort.MaxValue => 3,
        <= uint.MaxValue => 5,
        _ => 9
    };

    private static ulong Argument(ReadOnlySpan<byte> item)
    {
        int end = Header(item);
        ulong result = end == 1 ? (ulong)(item[0] & 31) : 0;
        for (int i = 1; i < end; i++)
        {
            result = (result << 8) | item[i];
        }
        return result;
    }

    private static List<ReadOnlyMemory<byte>> Children(ReadOnlyMemory<byte> item)
    {
        int start = Header(item.Span);
        CborReader reader = new(item.Span[start..((item.Span[0] & 31) == 31 ? ^1 : ^0)]);
        List<ReadOnlyMemory<byte>> children = [];
        while (!reader.Buffer.IsEmpty)
        {
            int length = reader.ReadDataItem().Length;
            children.Add(item.Slice(start, length));
            start += length;
        }
        return children;
    }

    private static string? Compare(ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after,
        SortedSet<string> classes, string path, int depth, bool byteStringChunk = false)
    {
        ReadOnlySpan<byte> a = before.Span;
        ReadOnlySpan<byte> b = after.Span;
        if (a.SequenceEqual(b))
        {
            return null;
        }
        int type = a[0] >> 5;
        string error = $"{path} ({Convert.ToHexString(a[..Math.Min(8, a.Length)])}/{Convert.ToHexString(b[..Math.Min(8, b.Length)])})";
        if (depth > 128 || type != b[0] >> 5 || type == 7)
        {
            return error;
        }
        bool indefiniteA = (a[0] & 31) == 31;
        bool indefiniteB = (b[0] & 31) == 31;
        bool chunkedBytes = type == 2 && indefiniteA && indefiniteB;
        if (!chunkedBytes && type is not (4 or 5 or 6))
        {
            if (!byteStringChunk || type != 2 || indefiniteA || indefiniteB ||
                Header(a) <= Header(b) || Header(b) != PreferredHeader(Argument(b)) ||
                !a[Header(a)..].SequenceEqual(b[Header(b)..]))
            {
                return error;
            }
            _ = classes.Add("argument-width");
            return null;
        }
        // Preserving framing is required for every leaf and tag. At a changed container,
        // compare the children and allow only the definite/indefinite prefix transition.
        if (indefiniteA != indefiniteB)
        {
            ReadOnlySpan<byte> definite = indefiniteA ? b : a;
            if (Header(definite) != PreferredHeader(Argument(definite)))
            {
                return error;
            }
            _ = classes.Add(type == 4 ? "array-framing" : "map-framing");
        }
        else if (!indefiniteA)
        {
            if (!a[..Header(a)].SequenceEqual(b[..Header(b)]))
            {
                return error;
            }
        }
        List<ReadOnlyMemory<byte>> leftChildren = Children(before);
        List<ReadOnlyMemory<byte>> rightChildren = Children(after);
        if (leftChildren.Count != rightChildren.Count)
        {
            return $"{error}: child count {leftChildren.Count}/{rightChildren.Count}";
        }
        if (type == 5 && !Enumerable.Range(0, leftChildren.Count / 2).All(i =>
            leftChildren[i * 2].Span.SequenceEqual(rightChildren[i * 2].Span)))
        {
            Dictionary<string, int>? keys = UniqueKeys(rightChildren);
            Dictionary<string, int>? beforeKeys = UniqueKeys(leftChildren);
            if (keys is null || beforeKeys is null || !beforeKeys.Keys.All(keys.ContainsKey))
            {
                return error;
            }
            rightChildren = [.. beforeKeys.Keys.SelectMany(key => rightChildren.GetRange(keys[key], 2))];
            _ = classes.Add("map-order");
        }
        for (int i = 0; i < leftChildren.Count; i++)
        {
            if (Compare(leftChildren[i], rightChildren[i], classes, $"{path}/{i}", depth + 1, chunkedBytes) is string mismatch)
            {
                return mismatch;
            }
        }
        return null;
    }

    // Deliberately do not reorder maps with duplicate keys or composite keys.
    private static Dictionary<string, int>? UniqueKeys(List<ReadOnlyMemory<byte>> children)
    {
        Dictionary<string, int> keys = [];
        for (int i = 0; i < children.Count; i += 2)
        {
            ReadOnlySpan<byte> key = children[i].Span;
            int type = key[0] >> 5;
            if (type > 3 || (key[0] & 31) == 31 || !keys.TryAdd($"{type}:{Argument(key)}:{Convert.ToHexString(key[Header(key)..])}", i))
            {
                return null;
            }
        }
        return keys;
    }
}
