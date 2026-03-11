using System.Text;
using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class StringBuiltins
{
    internal static CekValue AppendString(CekValue[] args)
    {
        return StringResult(UnwrapString(args[0]) + UnwrapString(args[1]));
    }

    internal static CekValue EqualsString(CekValue[] args)
    {
        return BoolResult(UnwrapString(args[0]) == UnwrapString(args[1]));
    }

    internal static CekValue EncodeUtf8(CekValue[] args)
    {
        return ByteStringResult(Encoding.UTF8.GetBytes(UnwrapString(args[0])));
    }

    internal static CekValue DecodeUtf8(CekValue[] args)
    {
        ReadOnlyMemory<byte> bs = UnwrapByteString(args[0]);
        try
        {
            string s = Encoding.UTF8.GetString(bs.Span);
            // Verify no replacement characters from invalid sequences
            if (bs.Length > 0 && s.Contains('\uFFFD', StringComparison.Ordinal))
            {
                // Double-check: re-encode and compare
                byte[] reEncoded = Encoding.UTF8.GetBytes(s);
                if (!bs.Span.SequenceEqual(reEncoded))
                {
                    throw new EvaluationException("decodeUtf8: invalid UTF-8 byte sequence");
                }
            }

            return StringResult(s);
        }
        catch (DecoderFallbackException)
        {
            throw new EvaluationException("decodeUtf8: invalid UTF-8 byte sequence");
        }
    }
}
