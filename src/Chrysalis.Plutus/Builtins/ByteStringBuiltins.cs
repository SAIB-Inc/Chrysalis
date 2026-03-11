using System.Numerics;
using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ByteStringBuiltins
{
    internal static CekValue AppendByteString(CekValue[] args)
    {
        ReadOnlyMemory<byte> a = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> b = UnwrapByteString(args[1]);
        byte[] result = new byte[a.Length + b.Length];
        a.Span.CopyTo(result);
        b.Span.CopyTo(result.AsSpan(a.Length));
        return ByteStringResult(result);
    }

    internal static CekValue ConsByteString(CekValue[] args)
    {
        BigInteger n = UnwrapInteger(args[0]);
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[1]);
        if (n < 0 || n > 255)
        {
            throw new EvaluationException(
                $"consByteString: byte value out of range [0, 255]: {n}");
        }

        byte[] result = new byte[bs.Length + 1];
        result[0] = (byte)n;
        bs.Span.CopyTo(result.AsSpan(1));
        return ByteStringResult(result);
    }

    internal static CekValue SliceByteString(CekValue[] args)
    {
        BigInteger skipBig = UnwrapInteger(args[0]);
        BigInteger takeBig = UnwrapInteger(args[1]);
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[2]);

        int skip = ClampToNonNegative(skipBig, bs.Length);
        int take = ClampToNonNegative(takeBig, bs.Length - skip);
        return ByteStringResult(bs.Slice(skip, take).ToArray());
    }

    internal static CekValue LengthOfByteString(CekValue[] args) => IntegerResult(UnwrapByteString(args[0]).Length);

    internal static CekValue IndexByteString(CekValue[] args)
    {
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[0]);
        BigInteger idxBig = UnwrapInteger(args[1]);
        return idxBig < 0 || idxBig >= bs.Length
            ? throw new EvaluationException(
                $"indexByteString: index out of bounds: {idxBig}, length: {bs.Length}")
            : IntegerResult(bs.Span[(int)idxBig]);
    }

    internal static CekValue EqualsByteString(CekValue[] args) => BoolResult(UnwrapByteString(args[0]).Span.SequenceEqual(UnwrapByteString(args[1]).Span));

    internal static CekValue LessThanByteString(CekValue[] args) => BoolResult(UnwrapByteString(args[0]).Span.SequenceCompareTo(UnwrapByteString(args[1]).Span) < 0);

    internal static CekValue LessThanEqualsByteString(CekValue[] args) => BoolResult(UnwrapByteString(args[0]).Span.SequenceCompareTo(UnwrapByteString(args[1]).Span) <= 0);

    private static int ClampToNonNegative(BigInteger n, int max) => n < 0 ? 0 : n > max ? max : (int)n;
}
