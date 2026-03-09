using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// PlutusV4 value builtins.
/// Ported from blaze-plutus src/cek/builtins/value.ts (MIT License).
/// </summary>
internal static class ValueBuiltins
{
    private static readonly BigInteger QuantityMax = (BigInteger.One << 127) - 1;
    private static readonly BigInteger QuantityMin = -(BigInteger.One << 127);
    private const int MaxKeyLen = 32;

    private static void CheckQuantityRange(BigInteger q)
    {
        if (q < QuantityMin || q > QuantityMax)
        {
            throw new EvaluationException($"quantity out of 128-bit signed range");
        }
    }

    private static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] < b[i])
            {
                return -1;
            }

            if (a[i] > b[i])
            {
                return 1;
            }
        }
        return a.Length - b.Length;
    }

    private static bool BytesEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) =>
        a.SequenceEqual(b);

    // --- lookupCoin ---

    private static BigInteger LookupCoinImpl(
        ReadOnlyMemory<byte> ccy, ReadOnlyMemory<byte> token, LedgerValue v)
    {
        foreach (CurrencyEntry entry in v.Entries)
        {
            if (BytesEqual(entry.Currency.Span, ccy.Span))
            {
                foreach (TokenEntry tok in entry.Tokens)
                {
                    if (BytesEqual(tok.Name.Span, token.Span))
                    {
                        return tok.Quantity;
                    }
                }
                return BigInteger.Zero;
            }
        }
        return BigInteger.Zero;
    }

    internal static CekValue LookupCoin(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> ccy = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> token = UnwrapByteString(args[1]);
        LedgerValue v = UnwrapValue(args[2]);
        return IntegerResult(LookupCoinImpl(ccy, token, v));
    }

    // --- insertCoin ---

    private static LedgerValue DeleteToken(
        LedgerValue v, ReadOnlyMemory<byte> ccy, ReadOnlyMemory<byte> token)
    {
        ImmutableArray<CurrencyEntry>.Builder newEntries =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        foreach (CurrencyEntry entry in v.Entries)
        {
            if (BytesEqual(entry.Currency.Span, ccy.Span))
            {
                ImmutableArray<TokenEntry>.Builder newTokens =
                    ImmutableArray.CreateBuilder<TokenEntry>();
                foreach (TokenEntry tok in entry.Tokens)
                {
                    if (!BytesEqual(tok.Name.Span, token.Span))
                    {
                        newTokens.Add(tok);
                    }
                }
                if (newTokens.Count > 0)
                {
                    newEntries.Add(new CurrencyEntry(entry.Currency, newTokens.ToImmutable()));
                }
            }
            else
            {
                newEntries.Add(entry);
            }
        }
        return new LedgerValue(newEntries.ToImmutable());
    }

    private static LedgerValue InsertToken(
        LedgerValue v, ReadOnlyMemory<byte> ccy, ReadOnlyMemory<byte> token, BigInteger qty)
    {
        ImmutableArray<CurrencyEntry>.Builder newEntries =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        bool inserted = false;

        foreach (CurrencyEntry entry in v.Entries)
        {
            int cmp = CompareBytes(entry.Currency.Span, ccy.Span);
            if (cmp < 0)
            {
                newEntries.Add(entry);
            }
            else if (cmp == 0)
            {
                // Found the currency — insert/replace token in sorted position
                ImmutableArray<TokenEntry>.Builder newTokens =
                    ImmutableArray.CreateBuilder<TokenEntry>();
                bool tokenInserted = false;
                foreach (TokenEntry tok in entry.Tokens)
                {
                    int tcmp = CompareBytes(tok.Name.Span, token.Span);
                    if (tcmp < 0)
                    {
                        newTokens.Add(tok);
                    }
                    else if (tcmp == 0)
                    {
                        newTokens.Add(new TokenEntry(token, qty));
                        tokenInserted = true;
                    }
                    else
                    {
                        if (!tokenInserted)
                        {
                            newTokens.Add(new TokenEntry(token, qty));
                            tokenInserted = true;
                        }
                        newTokens.Add(tok);
                    }
                }
                if (!tokenInserted)
                {
                    newTokens.Add(new TokenEntry(token, qty));
                }
                newEntries.Add(new CurrencyEntry(entry.Currency, newTokens.ToImmutable()));
                inserted = true;
            }
            else
            {
                if (!inserted)
                {
                    newEntries.Add(new CurrencyEntry(ccy,
                        [new TokenEntry(token, qty)]));
                    inserted = true;
                }
                newEntries.Add(entry);
            }
        }

        if (!inserted)
        {
            newEntries.Add(new CurrencyEntry(ccy,
                [new TokenEntry(token, qty)]));
        }

        return new LedgerValue(newEntries.ToImmutable());
    }

    internal static CekValue InsertCoin(ImmutableArray<CekValue> args)
    {
        ReadOnlyMemory<byte> ccy = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> token = UnwrapByteString(args[1]);
        BigInteger qty = UnwrapInteger(args[2]);
        LedgerValue v = UnwrapValue(args[3]);

        if (qty == 0)
        {
            return ccy.Length > MaxKeyLen || token.Length > MaxKeyLen
                ? ValueResult(v)
                : ValueResult(DeleteToken(v, ccy, token));
        }

        if (ccy.Length > MaxKeyLen)
        {
            throw new EvaluationException(
                $"insertCoin: currency key too long ({ccy.Length} bytes)");
        }
        if (token.Length > MaxKeyLen)
        {
            throw new EvaluationException(
                $"insertCoin: token key too long ({token.Length} bytes)");
        }
        CheckQuantityRange(qty);

        return ValueResult(InsertToken(v, ccy, token, qty));
    }

    // --- unionValue ---

    private static ImmutableArray<TokenEntry> MergeTokenLists(
        ImmutableArray<TokenEntry> a, ImmutableArray<TokenEntry> b)
    {
        ImmutableArray<TokenEntry>.Builder result =
            ImmutableArray.CreateBuilder<TokenEntry>();
        int i = 0, j = 0;

        while (i < a.Length && j < b.Length)
        {
            int cmp = CompareBytes(a[i].Name.Span, b[j].Name.Span);
            if (cmp < 0)
            {
                result.Add(a[i]);
                i++;
            }
            else if (cmp > 0)
            {
                result.Add(b[j]);
                j++;
            }
            else
            {
                BigInteger sum = a[i].Quantity + b[j].Quantity;
                CheckQuantityRange(sum);
                if (sum != 0)
                {
                    result.Add(new TokenEntry(a[i].Name, sum));
                }
                i++;
                j++;
            }
        }

        while (i < a.Length)
        {
            result.Add(a[i]);
            i++;
        }
        while (j < b.Length)
        {
            result.Add(b[j]);
            j++;
        }

        return result.ToImmutable();
    }

    internal static CekValue UnionValue(ImmutableArray<CekValue> args)
    {
        LedgerValue v1 = UnwrapValue(args[0]);
        LedgerValue v2 = UnwrapValue(args[1]);

        ImmutableArray<CurrencyEntry>.Builder result =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        int i = 0, j = 0;

        while (i < v1.Entries.Length && j < v2.Entries.Length)
        {
            int cmp = CompareBytes(v1.Entries[i].Currency.Span, v2.Entries[j].Currency.Span);
            if (cmp < 0)
            {
                result.Add(v1.Entries[i]);
                i++;
            }
            else if (cmp > 0)
            {
                result.Add(v2.Entries[j]);
                j++;
            }
            else
            {
                ImmutableArray<TokenEntry> merged =
                    MergeTokenLists(v1.Entries[i].Tokens, v2.Entries[j].Tokens);
                if (merged.Length > 0)
                {
                    result.Add(new CurrencyEntry(v1.Entries[i].Currency, merged));
                }
                i++;
                j++;
            }
        }

        while (i < v1.Entries.Length)
        {
            result.Add(v1.Entries[i]);
            i++;
        }
        while (j < v2.Entries.Length)
        {
            result.Add(v2.Entries[j]);
            j++;
        }

        return ValueResult(new LedgerValue(result.ToImmutable()));
    }

    // --- valueContains ---

    internal static CekValue ValueContains(ImmutableArray<CekValue> args)
    {
        LedgerValue v1 = UnwrapValue(args[0]);
        LedgerValue v2 = UnwrapValue(args[1]);

        foreach (CurrencyEntry entry in v1.Entries)
        {
            foreach (TokenEntry tok in entry.Tokens)
            {
                if (tok.Quantity < 0)
                {
                    throw new EvaluationException(
                        "valueContains: negative quantity in first value");
                }
            }
        }
        foreach (CurrencyEntry entry in v2.Entries)
        {
            foreach (TokenEntry tok in entry.Tokens)
            {
                if (tok.Quantity < 0)
                {
                    throw new EvaluationException(
                        "valueContains: negative quantity in second value");
                }
            }
        }

        foreach (CurrencyEntry entry in v2.Entries)
        {
            foreach (TokenEntry tok in entry.Tokens)
            {
                BigInteger v1Qty = LookupCoinImpl(entry.Currency, tok.Name, v1);
                if (v1Qty < tok.Quantity)
                {
                    return BoolResult(false);
                }
            }
        }

        return BoolResult(true);
    }

    // --- scaleValue ---

    internal static CekValue ScaleValue(ImmutableArray<CekValue> args)
    {
        BigInteger scalar = UnwrapInteger(args[0]);
        LedgerValue v = UnwrapValue(args[1]);

        if (scalar == 0)
        {
            return ValueResult(new LedgerValue([]));
        }

        ImmutableArray<CurrencyEntry>.Builder newEntries =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        foreach (CurrencyEntry entry in v.Entries)
        {
            ImmutableArray<TokenEntry>.Builder newTokens =
                ImmutableArray.CreateBuilder<TokenEntry>();
            foreach (TokenEntry tok in entry.Tokens)
            {
                BigInteger product = tok.Quantity * scalar;
                CheckQuantityRange(product);
                if (product != 0)
                {
                    newTokens.Add(new TokenEntry(tok.Name, product));
                }
            }
            if (newTokens.Count > 0)
            {
                newEntries.Add(new CurrencyEntry(entry.Currency, newTokens.ToImmutable()));
            }
        }

        return ValueResult(new LedgerValue(newEntries.ToImmutable()));
    }

    // --- valueData ---

    internal static CekValue ValueData(ImmutableArray<CekValue> args)
    {
        LedgerValue v = UnwrapValue(args[0]);

        ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder outerEntries =
            ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>();

        foreach (CurrencyEntry entry in v.Entries)
        {
            ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder innerEntries =
                ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>();
            foreach (TokenEntry tok in entry.Tokens)
            {
                innerEntries.Add((
                    new PlutusDataByteString(tok.Name),
                    new PlutusDataInteger(tok.Quantity)));
            }
            outerEntries.Add((
                new PlutusDataByteString(entry.Currency),
                new PlutusDataMap(innerEntries.ToImmutable())));
        }

        return DataResult(new PlutusDataMap(outerEntries.ToImmutable()));
    }

    // --- unValueData ---

    internal static CekValue UnValueData(ImmutableArray<CekValue> args)
    {
        PlutusData d = UnwrapData(args[0]);

        if (d is not PlutusDataMap outerMap)
        {
            throw new EvaluationException(
                $"unValueData: expected map data, got {d.GetType().Name}");
        }

        ImmutableArray<CurrencyEntry>.Builder entries =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        ReadOnlyMemory<byte> prevCurrency = default;
        bool hasPrev = false;

        foreach ((PlutusData key, PlutusData val) in outerMap.Entries)
        {
            if (key is not PlutusDataByteString ccyBs)
            {
                throw new EvaluationException(
                    $"unValueData: expected bytestring currency key, got {key.GetType().Name}");
            }
            if (ccyBs.Value.Length > MaxKeyLen)
            {
                throw new EvaluationException(
                    $"unValueData: currency key too long ({ccyBs.Value.Length} bytes)");
            }

            if (hasPrev && CompareBytes(prevCurrency.Span, ccyBs.Value.Span) >= 0)
            {
                throw new EvaluationException(
                    "unValueData: currency keys not in strictly ascending order");
            }
            prevCurrency = ccyBs.Value;
            hasPrev = true;

            if (val is not PlutusDataMap innerMap)
            {
                throw new EvaluationException(
                    $"unValueData: expected map for token entries, got {val.GetType().Name}");
            }

            if (innerMap.Entries.Length == 0)
            {
                throw new EvaluationException("unValueData: empty token map");
            }

            ImmutableArray<TokenEntry>.Builder tokens =
                ImmutableArray.CreateBuilder<TokenEntry>();
            ReadOnlyMemory<byte> prevToken = default;
            bool hasTokenPrev = false;

            foreach ((PlutusData tKey, PlutusData tVal) in innerMap.Entries)
            {
                if (tKey is not PlutusDataByteString tokenBs)
                {
                    throw new EvaluationException(
                        $"unValueData: expected bytestring token key, got {tKey.GetType().Name}");
                }
                if (tokenBs.Value.Length > MaxKeyLen)
                {
                    throw new EvaluationException(
                        $"unValueData: token key too long ({tokenBs.Value.Length} bytes)");
                }

                if (hasTokenPrev && CompareBytes(prevToken.Span, tokenBs.Value.Span) >= 0)
                {
                    throw new EvaluationException(
                        "unValueData: token keys not in strictly ascending order");
                }
                prevToken = tokenBs.Value;
                hasTokenPrev = true;

                if (tVal is not PlutusDataInteger qtyData)
                {
                    throw new EvaluationException(
                        $"unValueData: expected integer quantity, got {tVal.GetType().Name}");
                }

                if (qtyData.Value == 0)
                {
                    throw new EvaluationException("unValueData: zero quantity");
                }

                CheckQuantityRange(qtyData.Value);
                tokens.Add(new TokenEntry(tokenBs.Value, qtyData.Value));
            }

            entries.Add(new CurrencyEntry(ccyBs.Value, tokens.ToImmutable()));
        }

        return ValueResult(new LedgerValue(entries.ToImmutable()));
    }
}
