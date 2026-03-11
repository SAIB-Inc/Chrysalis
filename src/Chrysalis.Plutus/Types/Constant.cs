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
public sealed record LedgerValue(ImmutableArray<CurrencyEntry> Entries);

public sealed record ValueConstant(LedgerValue Value) : Constant
{
    public override ConstantType ConstantType => ConstantType.PlutusValue;
}
