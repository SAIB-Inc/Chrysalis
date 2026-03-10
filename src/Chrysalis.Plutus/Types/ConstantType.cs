namespace Chrysalis.Plutus.Types;

/// <summary>
/// Type annotation for UPLC constants. Used during Flat encoding/decoding
/// to describe the type of a constant value (including nested list/pair/array types).
/// </summary>
public abstract record ConstantType
{
    public static readonly ConstantType PlutusInteger = new IntegerType();
    public static readonly ConstantType PlutusByteString = new ByteStringType();
    public static readonly ConstantType PlutusText = new StringType();
    public static readonly ConstantType PlutusUnit = new UnitType();
    public static readonly ConstantType PlutusBool = new BoolType();
    public static readonly ConstantType PlutusData = new DataType();
    public static readonly ConstantType Bls12381G1Element = new Bls12381G1Type();
    public static readonly ConstantType Bls12381G2Element = new Bls12381G2Type();
    public static readonly ConstantType Bls12381MlResult = new Bls12381MlResultType();
    public static readonly ConstantType PlutusValue = new PlutusValueType();
}

public sealed record IntegerType : ConstantType;
public sealed record ByteStringType : ConstantType;
public sealed record StringType : ConstantType;
public sealed record UnitType : ConstantType;
public sealed record BoolType : ConstantType;
public sealed record DataType : ConstantType;
public sealed record Bls12381G1Type : ConstantType;
public sealed record Bls12381G2Type : ConstantType;
public sealed record Bls12381MlResultType : ConstantType;
public sealed record PlutusValueType : ConstantType;
public sealed record ListType(ConstantType Element) : ConstantType;
public sealed record ArrayType(ConstantType Element) : ConstantType;
public sealed record PairType(ConstantType First, ConstantType Second) : ConstantType;
