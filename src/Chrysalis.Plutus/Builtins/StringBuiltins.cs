using System.Collections.Immutable;
using System.Text;
using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class StringBuiltins
{
    internal static CekValue AppendString(ImmutableArray<CekValue> args) =>
        StringResult(UnwrapString(args[0]) + UnwrapString(args[1]));

    internal static CekValue EqualsString(ImmutableArray<CekValue> args) =>
        BoolResult(UnwrapString(args[0]) == UnwrapString(args[1]));

    internal static CekValue EncodeUtf8(ImmutableArray<CekValue> args) =>
        ByteStringResult(Encoding.UTF8.GetBytes(UnwrapString(args[0])));

    internal static CekValue DecodeUtf8(ImmutableArray<CekValue> args)
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
