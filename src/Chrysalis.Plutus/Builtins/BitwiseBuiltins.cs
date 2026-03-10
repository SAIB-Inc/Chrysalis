using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class BitwiseBuiltins
{
    private const int MaxOutputLength = 8192;

    // --- Logical bitwise ops ---

    internal static CekValue AndByteString(ImmutableArray<CekValue> args)
    {
        return BitwiseOp(args, (a, b) => (byte)(a & b), 0xFF);
    }

    internal static CekValue OrByteString(ImmutableArray<CekValue> args)
    {
        return BitwiseOp(args, (a, b) => (byte)(a | b), 0x00);
    }

    internal static CekValue XorByteString(ImmutableArray<CekValue> args)
    {
        return BitwiseOp(args, (a, b) => (byte)(a ^ b), 0x00);
    }

    internal static CekValue ComplementByteString(ImmutableArray<CekValue> args)
    {
        ReadOnlySpan<byte> bs = UnwrapByteString(args[0]).Span;
        byte[] result = new byte[bs.Length];
        for (int i = 0; i < bs.Length; i++)
        {
            result[i] = (byte)(bs[i] ^ 0xFF);
        }

        return ByteStringResult(result);
    }

    // --- Shift & Rotate ---

    internal static CekValue ShiftByteString(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> bsMem = UnwrapByteString(args[0]);
        BigInteger shiftAmount = UnwrapInteger(args[1]);

        if (bsMem.Length == 0)
        {
            return ByteStringResult([]);
        }

        ReadOnlySpan<byte> bs = bsMem.Span;
        int totalBits = bs.Length * 8;

        if (shiftAmount > totalBits || shiftAmount < -totalBits)
        {
            return ByteStringResult(new byte[bs.Length]);
        }

        int shift = (int)shiftAmount;
        if (shift == 0)
        {
            return ByteStringResult(bsMem.ToArray());
        }

        byte[] result = new byte[bs.Length];

        if (shift > 0)
        {
            // Left shift (toward MSB) — MSB0 ordering
            for (int i = 0; i < totalBits - shift; i++)
            {
                int srcIdx = i + shift;
                if (((bs[srcIdx >>> 3] >> (7 - (srcIdx & 7))) & 1) != 0)
                {
                    result[i >>> 3] |= (byte)(1 << (7 - (i & 7)));
                }
            }
        }
        else
        {
            // Right shift (toward LSB) — MSB0 ordering
            int absShift = -shift;
            for (int i = absShift; i < totalBits; i++)
            {
                int srcIdx = i - absShift;
                if (((bs[srcIdx >>> 3] >> (7 - (srcIdx & 7))) & 1) != 0)
                {
                    result[i >>> 3] |= (byte)(1 << (7 - (i & 7)));
                }
            }
        }

        return ByteStringResult(result);
    }

    internal static CekValue RotateByteString(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> bsMem = UnwrapByteString(args[0]);
        BigInteger rotAmount = UnwrapInteger(args[1]);

        if (bsMem.Length == 0)
        {
            return ByteStringResult([]);
        }

        ReadOnlySpan<byte> bs = bsMem.Span;
        BigInteger totalBits = bs.Length * 8;

        BigInteger normalized = ((rotAmount % totalBits) + totalBits) % totalBits;
        if (normalized == 0)
        {
            return ByteStringResult(bsMem.ToArray());
        }

        int shift = (int)normalized;
        int byteShift = shift / 8;
        int bitShift = shift % 8;
        int len = bs.Length;

        byte[] result = new byte[len];
        for (int i = 0; i < len; i++)
        {
            int src = (i + byteShift) % len;
            int next = (src + 1) % len;
            result[i] = (byte)(((bs[src] << bitShift) | (bs[next] >>> (8 - bitShift))) & 0xFF);
        }

        return ByteStringResult(result);
    }

    // --- Bit access ---

    internal static CekValue ReadBit(ImmutableArray<CekValue> args)
    {
        ReadOnlySpan<byte> bs = UnwrapByteString(args[0]).Span;
        BigInteger idx = UnwrapInteger(args[1]);

        if (idx < 0)
        {
            throw new EvaluationException("readBit: negative index");
        }

        BigInteger totalBits = bs.Length * 8;
        if (idx >= totalBits)
        {
            throw new EvaluationException("readBit: index out of bounds");
        }

        int bitIdx = (int)idx;
        int byteIndex = bitIdx >>> 3;
        int bitOffset = bitIdx & 7;
        int flippedIndex = bs.Length - 1 - byteIndex;
        return BoolResult(((bs[flippedIndex] >>> bitOffset) & 1) == 1);
    }

    internal static CekValue WriteBits(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> bsMem = UnwrapByteString(args[0]);
        ImmutableArray<Constant> indices = UnwrapList(args[1]);
        bool setVal = UnwrapBool(args[2]);

        ReadOnlySpan<byte> bs = bsMem.Span;
        BigInteger totalBits = bs.Length * 8;

        byte[] result = bsMem.ToArray();

        foreach (Constant c in indices)
        {
            if (c is not IntegerConstant ic)
            {
                throw new EvaluationException(
                    $"writeBits: expected integer in index list, got {c.GetType().Name}");
            }

            BigInteger idx = ic.Value;
            if (idx < 0 || idx >= totalBits)
            {
                throw new EvaluationException("writeBits: index out of bounds");
            }

            int bitIdx = (int)idx;
            int byteIndex = bitIdx >>> 3;
            int bitOffset = bitIdx & 7;
            int flippedIndex = bs.Length - 1 - byteIndex;

            if (setVal)
            {
                result[flippedIndex] |= (byte)(1 << bitOffset);
            }
            else
            {
                result[flippedIndex] &= (byte)~(1 << bitOffset);
            }
        }

        return ByteStringResult(result);
    }

    // --- Count / Find ---

    internal static CekValue CountSetBits(ImmutableArray<CekValue> args)
    {
        ReadOnlySpan<byte> bs = UnwrapByteString(args[0]).Span;
        int count = 0;
        for (int i = 0; i < bs.Length; i++)
        {
            count += int.PopCount(bs[i]);
        }

        return IntegerResult(count);
    }

    internal static CekValue FindFirstSetBit(ImmutableArray<CekValue> args)
    {
        ReadOnlySpan<byte> bs = UnwrapByteString(args[0]).Span;

        // Iterate from LSB end (last byte) backward
        for (int byteIdx = bs.Length - 1; byteIdx >= 0; byteIdx--)
        {
            byte b = bs[byteIdx];
            if (b != 0)
            {
                int ctz = int.TrailingZeroCount(b);
                int bitIndex = ctz + ((bs.Length - 1 - byteIdx) * 8);
                return IntegerResult(bitIndex);
            }
        }

        return IntegerResult(-1);
    }

    // --- Replicate ---

    internal static CekValue ReplicateByte(ImmutableArray<CekValue> args)
    {
        BigInteger size = UnwrapInteger(args[0]);
        BigInteger byteVal = UnwrapInteger(args[1]);

        if (size < 0)
        {
            throw new EvaluationException("replicateByte: negative size");
        }

        if (size > MaxOutputLength)
        {
            throw new EvaluationException("replicateByte: size exceeds 8192");
        }

        if (byteVal < 0 || byteVal > 255)
        {
            throw new EvaluationException("replicateByte: byte value not in [0, 255]");
        }

        byte[] result = new byte[(int)size];
        Array.Fill(result, (byte)byteVal);
        return ByteStringResult(result);
    }

    // --- Integer/ByteString conversion ---

    internal static CekValue IntegerToByteString(ImmutableArray<CekValue> args)
    {
        bool bigEndian = UnwrapBool(args[0]);
        BigInteger size = UnwrapInteger(args[1]);
        BigInteger input = UnwrapInteger(args[2]);

        if (input < 0)
        {
            throw new EvaluationException("integerToByteString: negative integer");
        }

        if (size < 0)
        {
            throw new EvaluationException("integerToByteString: negative size");
        }

        if (size > MaxOutputLength)
        {
            throw new EvaluationException("integerToByteString: size exceeds 8192");
        }

        int requestedSize = (int)size;

        // Convert to big-endian bytes
        byte[] significantBytes = input.IsZero ? [] : input.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (requestedSize == 0)
        {
            // Unbounded — minimal representation
            if (significantBytes.Length > MaxOutputLength)
            {
                throw new EvaluationException("integerToByteString: output exceeds 8192 bytes");
            }

            if (bigEndian)
            {
                return ByteStringResult(significantBytes);
            }

            // Reverse for little-endian
            Array.Reverse(significantBytes);
            return ByteStringResult(significantBytes);
        }

        // Bounded — must fit in requested size
        if (significantBytes.Length > requestedSize)
        {
            throw new EvaluationException(
                "integerToByteString: integer doesn't fit in requested size");
        }

        byte[] result = new byte[requestedSize];
        if (bigEndian)
        {
            // Pad with zeros on the left, significant bytes on the right
            int offset = requestedSize - significantBytes.Length;
            significantBytes.CopyTo(result.AsSpan(offset));
        }
        else
        {
            // Little-endian: reversed significant bytes on the left
            for (int i = 0; i < significantBytes.Length; i++)
            {
                result[i] = significantBytes[significantBytes.Length - 1 - i];
            }
        }

        return ByteStringResult(result);
    }

    internal static CekValue ByteStringToInteger(ImmutableArray<CekValue> args)
    {
        bool bigEndian = UnwrapBool(args[0]);
        ReadOnlyMemory<byte> bsMem = UnwrapByteString(args[1]);

        if (bsMem.Length == 0)
        {
            return IntegerResult(0);
        }

        ReadOnlySpan<byte> bs = bsMem.Span;

        if (bigEndian)
        {
            return IntegerResult(new BigInteger(bs, isUnsigned: true, isBigEndian: true));
        }

        // Little-endian
        return IntegerResult(new BigInteger(bs, isUnsigned: true, isBigEndian: false));
    }

    // --- Private helpers ---

    private static CekValue BitwiseOp(
        ImmutableArray<CekValue> args,
        Func<byte, byte, byte> op,
        byte padByte)
    {
        bool shouldPad = UnwrapBool(args[0]);
        ReadOnlySpan<byte> bs1 = UnwrapByteString(args[1]).Span;
        ReadOnlySpan<byte> bs2 = UnwrapByteString(args[2]).Span;

        ReadOnlySpan<byte> shorter = bs1.Length <= bs2.Length ? bs1 : bs2;
        ReadOnlySpan<byte> longer = bs1.Length <= bs2.Length ? bs2 : bs1;

        if (shouldPad)
        {
            byte[] result = new byte[longer.Length];
            for (int i = 0; i < longer.Length; i++)
            {
                byte a = longer[i];
                byte b = i < shorter.Length ? shorter[i] : padByte;
                result[i] = op(a, b);
            }

            return ByteStringResult(result);
        }
        else
        {
            int minLen = shorter.Length;
            byte[] result = new byte[minLen];
            for (int i = 0; i < minLen; i++)
            {
                result[i] = op(bs1[i], bs2[i]);
            }

            return ByteStringResult(result);
        }
    }
}
