// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Collections.Immutable;
using System.Numerics;

namespace Chrysalis.Plutus.Types;

/// <summary>
/// A UPLC constant value. Represents all built-in types supported by Plutus Core.
/// </summary>
public abstract record Constant
{
    public abstract ConstantType ConstantType { get; }
}

public sealed record IntegerConstant(BigInteger Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusInteger;
}

public sealed record ByteStringConstant(ReadOnlyMemory<byte> Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusByteString;
}

public sealed record StringConstant(string Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusText;
}

public sealed record BoolConstant(bool Value) : Constant
{
    public static readonly BoolConstant True = new(true);
    public static readonly BoolConstant False = new(false);
    public override ConstantType ConstantType => ConstantType.PlutusBool;
}

public sealed record UnitConstant : Constant
{
    public static readonly UnitConstant Instance = new();
    public override ConstantType ConstantType => ConstantType.PlutusUnit;
}

public sealed record ListConstant(ConstantType ItemType, ImmutableArray<Constant> Values) : Constant
{
    internal int Offset { get; init; }
    internal int Count => Values.Length - Offset;
    internal bool IsListEmpty => Offset >= Values.Length;
    internal Constant ElementAt(int index) => Values[Offset + index];
    public override ConstantType ConstantType => new ListType(ItemType);
}

public sealed record PairConstant(
    ConstantType FstType,
    ConstantType SndType,
    Constant First,
    Constant Second
) : Constant
{
    public override ConstantType ConstantType => new PairType(FstType, SndType);
}

public sealed record DataConstant(PlutusData Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusData;
}

public sealed record Bls12381G1Constant(ReadOnlyMemory<byte> Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.Bls12381G1Element;
}

public sealed record Bls12381G2Constant(ReadOnlyMemory<byte> Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.Bls12381G2Element;
}

public sealed record Bls12381MlResultConstant(ReadOnlyMemory<byte> Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.Bls12381MlResult;
}

public sealed record ArrayConstant(ConstantType ItemType, ImmutableArray<Constant> Values) : Constant
{
    public override ConstantType ConstantType => new ArrayType(ItemType);
}

public sealed record TokenEntry(ReadOnlyMemory<byte> Name, BigInteger Quantity);
public sealed record CurrencyEntry(ReadOnlyMemory<byte> Currency, ImmutableArray<TokenEntry> Tokens);
public sealed record LedgerValue(ImmutableArray<CurrencyEntry> Entries)
{
    internal const int MaxKeyLength = 32;
    internal static readonly BigInteger QuantityMax = (BigInteger.One << 127) - 1;
    internal static readonly BigInteger QuantityMin = -(BigInteger.One << 127);

    // The counts the cost model reads, each -1 until first read or handed on by Changed.
    private int _maxTokens = -1;
    private long _size = -1;
    private long _negatives = -1;

    // A record's copy constructor runs no field initializer, so a `with` copy sets its counts to unknown here.
    private LedgerValue(LedgerValue original)
    {
        Entries = original.Entries;
        _maxTokens = -1;
        _size = -1;
        _negatives = -1;
    }

    internal int MaxTokens => _maxTokens >= 0 ? _maxTokens : Measure().MaxTokens;

    internal long Size => _size >= 0 ? _size : Measure().Size;

    internal long Negatives => _negatives >= 0 ? _negatives : CountNegatives();

    internal (int MaxTokens, long Size, long Negatives) Kept => (_maxTokens, _size, _negatives);

    // The counts are not part of equality: these compare the array by reference, as the generated members did.
    public bool Equals(LedgerValue? other) => other is not null && Entries.Equals(other.Entries);

    public override int GetHashCode() => Entries.GetHashCode();

    // The first rule this value breaks, or null: plutus buildValueWith (Value.hs:692-757), keys compared as ByteString Ord.
    internal string? Violation()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            CurrencyEntry entry = Entries[i];
            if (entry.Currency.Length > MaxKeyLength)
            {
                return $"currency symbol longer than {MaxKeyLength} bytes";
            }
            if (i > 0 && Entries[i - 1].Currency.Span.SequenceCompareTo(entry.Currency.Span) >= 0)
            {
                return "currency symbols not strictly ascending";
            }
            if (entry.Tokens.IsEmpty)
            {
                return "empty inner map";
            }
            for (int j = 0; j < entry.Tokens.Length; j++)
            {
                TokenEntry token = entry.Tokens[j];
                if (token.Name.Length > MaxKeyLength)
                {
                    return $"token name longer than {MaxKeyLength} bytes";
                }
                if (j > 0 && entry.Tokens[j - 1].Name.Span.SequenceCompareTo(token.Name.Span) >= 0)
                {
                    return "token names not strictly ascending";
                }
                if (token.Quantity.IsZero)
                {
                    return "zero quantity";
                }
                if (token.Quantity < QuantityMin || token.Quantity > QuantityMax)
                {
                    return "quantity out of 128-bit signed range";
                }
            }
        }
        return null;
    }

    // The value after one token list went from oldTokens to newTokens entries (0 for a currency added or removed), with its new negative count.
    // It reads no count, so it never walks: an unknown count stays unknown, and the largest list shrinking makes the largest unknown.
    internal LedgerValue Changed(ImmutableArray<CurrencyEntry> entries, int oldTokens, int newTokens, long negatives) =>
        new(entries)
        {
            _size = _size < 0 ? -1 : _size + newTokens - oldTokens,
            _maxTokens = _maxTokens < 0 || (newTokens < oldTokens && oldTokens == _maxTokens) ? -1 : Math.Max(_maxTokens, newTokens),
            _negatives = negatives
        };

    private (int MaxTokens, long Size) Measure()
    {
        int maxTokens = 0;
        long size = 0;
        foreach (CurrencyEntry entry in Entries)
        {
            maxTokens = Math.Max(maxTokens, entry.Tokens.Length);
            size += entry.Tokens.Length;
        }
        _maxTokens = maxTokens;
        _size = size;
        return (maxTokens, size);
    }

    private long CountNegatives()
    {
        long negatives = 0;
        foreach (CurrencyEntry entry in Entries)
        {
            foreach (TokenEntry token in entry.Tokens)
            {
                negatives += token.Quantity.Sign < 0 ? 1 : 0;
            }
        }
        return _negatives = negatives;
    }
}

public sealed record ValueConstant(LedgerValue Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusValue;
}
