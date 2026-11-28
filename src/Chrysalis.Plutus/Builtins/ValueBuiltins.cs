// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// PlutusV4 value builtins.
/// Ported from blaze-plutus src/cek/builtins/value.ts.
/// </summary>
internal static class ValueBuiltins
{
    private static void CheckQuantityRange(BigInteger q)
    {
        if (q < LedgerValue.QuantityMin || q > LedgerValue.QuantityMax)
        {
            throw new EvaluationException($"quantity out of 128-bit signed range");
        }
    }

    // ByteString Ord (unsigned, shorter prefix first), as plutus's Map K searches by.
    private readonly struct ByteKey<T>(ReadOnlyMemory<byte> key, Func<T, ReadOnlyMemory<byte>> keyOf) : IComparable<T>
    {
        public int CompareTo(T? other) => key.Span.SequenceCompareTo(keyOf(other!).Span);
    }

    private static int FindCurrency(ImmutableArray<CurrencyEntry> entries, ReadOnlyMemory<byte> ccy) =>
        entries.AsSpan().BinarySearch(new ByteKey<CurrencyEntry>(ccy, static entry => entry.Currency));

    private static int FindToken(ImmutableArray<TokenEntry> tokens, ReadOnlyMemory<byte> name) =>
        tokens.AsSpan().BinarySearch(new ByteKey<TokenEntry>(name, static token => token.Name));

    // --- lookupCoin ---

    private static BigInteger LookupCoinImpl(
        ReadOnlyMemory<byte> ccy, ReadOnlyMemory<byte> token, LedgerValue v)
    {
        int ci = FindCurrency(v.Entries, ccy);
        if (ci < 0)
        {
            return BigInteger.Zero;
        }
        ImmutableArray<TokenEntry> tokens = v.Entries[ci].Tokens;
        int ti = FindToken(tokens, token);
        return ti < 0 ? BigInteger.Zero : tokens[ti].Quantity;
    }

    internal static CekValue LookupCoin(CekValue[] args)
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
        int ci = FindCurrency(v.Entries, ccy);
        if (ci < 0)
        {
            return v;
        }
        ImmutableArray<TokenEntry> tokens = v.Entries[ci].Tokens;
        int ti = FindToken(tokens, token);
        if (ti < 0)
        {
            return v;
        }
        long negatives = v.Negatives - (tokens[ti].Quantity.Sign < 0 ? 1 : 0);
        return tokens.Length == 1
            ? v.Changed(v.Entries.RemoveAt(ci), 1, 0, negatives)
            : v.Changed(v.Entries.SetItem(ci, new CurrencyEntry(v.Entries[ci].Currency, tokens.RemoveAt(ti))), tokens.Length, tokens.Length - 1, negatives);
    }

    private static LedgerValue InsertToken(
        LedgerValue v, ReadOnlyMemory<byte> ccy, ReadOnlyMemory<byte> token, BigInteger qty)
    {
        long negatives = v.Negatives + (qty.Sign < 0 ? 1 : 0);
        TokenEntry added = new(token, qty);
        int ci = FindCurrency(v.Entries, ccy);
        if (ci < 0)
        {
            return v.Changed(v.Entries.Insert(~ci, new CurrencyEntry(ccy, [added])), 0, 1, negatives);
        }
        CurrencyEntry entry = v.Entries[ci];
        int ti = FindToken(entry.Tokens, token);
        if (ti < 0)
        {
            return v.Changed(v.Entries.SetItem(ci, new CurrencyEntry(entry.Currency, entry.Tokens.Insert(~ti, added))), entry.Tokens.Length, entry.Tokens.Length + 1, negatives);
        }
        negatives -= entry.Tokens[ti].Quantity.Sign < 0 ? 1 : 0;
        return v.Changed(v.Entries.SetItem(ci, new CurrencyEntry(entry.Currency, entry.Tokens.SetItem(ti, added))), entry.Tokens.Length, entry.Tokens.Length, negatives);
    }

    internal static CekValue InsertCoin(CekValue[] args)
    {
        ReadOnlyMemory<byte> ccy = UnwrapByteString(args[0]);
        ReadOnlyMemory<byte> token = UnwrapByteString(args[1]);
        BigInteger qty = UnwrapInteger(args[2]);
        LedgerValue v = UnwrapValue(args[3]);

        if (qty == 0)
        {
            return ccy.Length > LedgerValue.MaxKeyLength || token.Length > LedgerValue.MaxKeyLength
                ? ValueResult(v)
                : ValueResult(DeleteToken(v, ccy, token));
        }

        if (ccy.Length > LedgerValue.MaxKeyLength)
        {
            throw new EvaluationException(
                $"insertCoin: currency key too long ({ccy.Length} bytes)");
        }
        if (token.Length > LedgerValue.MaxKeyLength)
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
            int cmp = a[i].Name.Span.SequenceCompareTo(b[j].Name.Span);
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

    internal static CekValue UnionValue(CekValue[] args)
    {
        LedgerValue v1 = UnwrapValue(args[0]);
        LedgerValue v2 = UnwrapValue(args[1]);

        ImmutableArray<CurrencyEntry>.Builder result =
            ImmutableArray.CreateBuilder<CurrencyEntry>();
        int i = 0, j = 0;

        while (i < v1.Entries.Length && j < v2.Entries.Length)
        {
            int cmp = v1.Entries[i].Currency.Span.SequenceCompareTo(v2.Entries[j].Currency.Span);
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

    // Value.hs:532-543: a negative count fails, in the first value and then in the second; a first value with fewer tokens than the second
    // is False at once, since the second then holds a token the first lacks and no quantity is zero or negative.
    internal static CekValue ValueContains(CekValue[] args)
    {
        LedgerValue v1 = UnwrapValue(args[0]);
        LedgerValue v2 = UnwrapValue(args[1]);

        if (v1.Negatives > 0)
        {
            throw new EvaluationException("valueContains: negative quantity in first value");
        }
        if (v2.Negatives > 0)
        {
            throw new EvaluationException("valueContains: negative quantity in second value");
        }
        if (v1.Size < v2.Size)
        {
            return BoolResult(false);
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

    internal static CekValue ScaleValue(CekValue[] args)
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

    // --- batch 7: policies, assetCount, keepPolicies, dropPolicies ---

    // Value.hs:462-469: the currency symbols in ascending order.
    internal static CekValue Policies(CekValue[] args)
    {
        LedgerValue v = UnwrapValue(args[0]);
        ImmutableArray<Constant>.Builder ids = ImmutableArray.CreateBuilder<Constant>(v.Entries.Length);
        foreach (CurrencyEntry entry in v.Entries)
        {
            ids.Add(new ByteStringConstant(entry.Currency));
        }
        return new VConstant(new ListConstant(ConstantType.PlutusByteString, ids.MoveToImmutable()));
    }

    internal static CekValue AssetCount(CekValue[] args) => IntegerResult(ExMem.ValueSizeExMem(UnwrapValue(args[0])));

    // Value.hs:466-512: both filters fail on a list of more than this many ids, repeats and over-long ids counted.
    private const int PolicyFilterMaxIds = 256;

    internal static CekValue KeepPolicies(CekValue[] args) => FilterPolicies(args, true, "keepPolicies");

    internal static CekValue DropPolicies(CekValue[] args) => FilterPolicies(args, false, "dropPolicies");

    // Each id is found by search in O(log m); a keep adds only the hit entries, a drop copies the runs between them.
    private static CekValue FilterPolicies(CekValue[] args, bool keep, string name)
    {
        ListConstant ids = UnwrapListConstant(args[0]);
        LedgerValue v = UnwrapValue(args[1]);
        if (ids.Count > PolicyFilterMaxIds)
        {
            throw new EvaluationException($"{name}: maximum number of policy ids ({PolicyFilterMaxIds}) exceeded");
        }

        Span<int> hits = stackalloc int[ids.Count];
        int found = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            int at = ids.ElementAt(i) is ByteStringConstant id
                ? FindCurrency(v.Entries, id.Value)
                : throw new EvaluationException($"{name}: expected a bytestring policy id");
            if (at >= 0)
            {
                hits[found++] = at;
            }
        }
        if (!keep && found == 0)
        {
            return args[1];
        }

        hits = hits[..found];
        hits.Sort();
        ImmutableArray<CurrencyEntry>.Builder result =
            ImmutableArray.CreateBuilder<CurrencyEntry>(keep ? found : Math.Max(0, v.Entries.Length - found));
        int start = 0;
        foreach (int hit in hits)
        {
            if (hit < start)
            {
                continue;
            }
            if (keep)
            {
                result.Add(v.Entries[hit]);
            }
            else
            {
                result.AddRange(v.Entries.AsSpan(start..hit));
            }
            start = hit + 1;
        }
        if (!keep)
        {
            result.AddRange(v.Entries.AsSpan(start..));
        }
        return ValueResult(new LedgerValue(result.DrainToImmutable()));
    }

    // --- valueData ---

    // Value.hs:585-598: valueData fails when the value holds more tokens than this.
    private const int ValueDataMaxSize = 40_000;

    internal static CekValue ValueData(CekValue[] args)
    {
        LedgerValue v = UnwrapValue(args[0]);
        if (ExMem.ValueSizeExMem(v) > ValueDataMaxSize)
        {
            throw new EvaluationException($"valueData: maximum input size ({ValueDataMaxSize}) exceeded");
        }

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

    internal static CekValue UnValueData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);

        if (d is not PlutusDataMap outerMap)
        {
            throw new EvaluationException(
                $"unValueData: expected map data, got {d.GetType().Name}");
        }

        ImmutableArray<CurrencyEntry>.Builder entries =
            ImmutableArray.CreateBuilder<CurrencyEntry>(outerMap.Entries.Length);
        foreach ((PlutusData key, PlutusData val) in outerMap.Entries)
        {
            if (key is not PlutusDataByteString currency || val is not PlutusDataMap innerMap)
            {
                throw new EvaluationException("unValueData: expected a (bytestring, map) entry");
            }

            ImmutableArray<TokenEntry>.Builder tokens =
                ImmutableArray.CreateBuilder<TokenEntry>(innerMap.Entries.Length);
            foreach ((PlutusData tKey, PlutusData tVal) in innerMap.Entries)
            {
                if (tKey is not PlutusDataByteString name || tVal is not PlutusDataInteger quantity)
                {
                    throw new EvaluationException("unValueData: expected a (bytestring, integer) entry");
                }
                tokens.Add(new TokenEntry(name.Value, quantity.Value));
            }
            entries.Add(new CurrencyEntry(currency.Value, tokens.MoveToImmutable()));
        }

        LedgerValue value = new(entries.MoveToImmutable());
        string? violation = value.Violation();
        return violation is null
            ? ValueResult(value)
            : throw new EvaluationException($"unValueData: {violation}");
    }
}
