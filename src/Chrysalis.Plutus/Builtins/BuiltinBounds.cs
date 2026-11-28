using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Types.DefaultFunction;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// Upstream argument bounds checked at unlifting (before builtin cost) and post-charge denotation limits.
/// Sources: PlutusCore/Default/Builtins.hs, Default/Universe.hs, Default/Universe/Cardano.hs,
/// Builtin/KnownType.hs, Bitwise.hs, BLS12_381/G1.hs and Bounds.hs (1.70.0.0 and 1.71.0.0).
/// </summary>
internal static class BuiltinBounds
{
    private const int None = 0;
    private const int CInteger = 1;
    private const int CByteString = 2;
    private const int Int64 = 3;
    private const int Word64 = 4;
    private const int Word8 = 5;
    private const int ListOfData = 6;
    private const int ListOfPairData = 7;
    private const int ListOfInteger = 8;
    private const int ListOfG1 = 9;
    private const int ListOfG2 = 10;

    // Indexed by (kind - ListOfData). PairType is shared to avoid per-call allocation.
    private static readonly ConstantType[] ListItemTypes =
    [
        ConstantType.PlutusData,
        new PairType(ConstantType.PlutusData, ConstantType.PlutusData),
        ConstantType.PlutusInteger,
        ConstantType.Bls12381G1Element,
        ConstantType.Bls12381G2Element
    ];

    internal static void CheckArguments(DefaultFunction func, CekValue[] args, Variant variant)
    {
        int kinds = Kinds(func, variant);
        for (int i = 0; kinds != 0; i++, kinds >>= 4)
        {
            int kind = kinds & 0xF;
            if (kind != None && !InBounds(kind, args[i]))
            {
                throw new EvaluationException(Message(kind));
            }
        }
    }

    internal static void CheckCharged(DefaultFunction func, CekValue[] args, Variant variant)
    {
        if (variant < Variant.D)
        {
            return;
        }

        if (func == WriteBits)
        {
            if (args[0] is VConstant { Value: ByteStringConstant bs } && bs.Value.Length > BitwiseBuiltins.MaxInputLength)
            {
                throw new EvaluationException($"writeBits: input too long (maximum is {BitwiseBuiltins.MaxInputLength} bytes)");
            }
        }
        else if (func is Bls12_381_G1_ScalarMul or Bls12_381_G2_ScalarMul
            && args[0] is VConstant { Value: IntegerConstant scalar })
        {
            BlsBuiltins.ValidateMsmScalar(scalar.Value);
        }
    }

    private static int Pack(int a0, int a1 = None, int a2 = None) => a0 | (a1 << 4) | (a2 << 8);

    private static int Kinds(DefaultFunction func, Variant v)
    {
        // Default/Builtins.hs:2806-2820: ensurableInteger is D and E, ensurableByteString is D to G.
        bool ints = v is Variant.D or Variant.E;
        bool bytes = v >= Variant.D;
        return (int)func switch
        {
            (int)AddInteger or (int)SubtractInteger or (int)MultiplyInteger or (int)DivideInteger or (int)QuotientInteger
                or (int)RemainderInteger or (int)ModInteger or (int)LessThanInteger or (int)LessThanEqualsInteger => ints ? Pack(CInteger, CInteger) : None,
            (int)AppendByteString or (int)EqualsByteString or (int)LessThanByteString or (int)LessThanEqualsByteString => bytes ? Pack(CByteString, CByteString) : None,
            (int)AndByteString or (int)OrByteString or (int)XorByteString => bytes ? Pack(None, CByteString, CByteString) : None,
            (int)Sha2_256 or (int)Sha3_256 or (int)Blake2b_256 or (int)Keccak_256 or (int)Blake2b_224 or (int)Ripemd_160
                or (int)DecodeUtf8 or (int)ComplementByteString or (int)CountSetBits or (int)FindFirstSetBit
                or (int)Bls12_381_G1_HashToGroup or (int)Bls12_381_G2_HashToGroup => bytes ? Pack(CByteString) : None,
            (int)IndexByteString or (int)ReadBit => Pack(bytes ? CByteString : None, Int64),
            (int)ByteStringToInteger or (int)VerifyEd25519Signature or (int)VerifySchnorrSecp256k1Signature => bytes ? Pack(None, CByteString) : None,
            (int)ConsByteString => v switch
            {
                Variant.C => Pack(Word8),
                Variant.D => Pack(CInteger, CByteString),
                Variant.E or Variant.G => Pack(Word8, CByteString),
                Variant.F => Pack(None, CByteString),
                Variant.A or Variant.B or _ => None
            },
            (int)SliceByteString => Pack(Int64, Int64, bytes ? CByteString : None),
            (int)ReplicateByte => Pack(None, Word8),
            (int)IndexArray => Pack(None, Int64),
            (int)ShiftByteString or (int)RotateByteString => bytes ? Pack(None, Int64) : None,
            (int)ConstrData => Pack(bytes ? Word64 : None, ListOfData),
            (int)MapData => Pack(ListOfPairData),
            (int)ListData => Pack(ListOfData),
            (int)WriteBits => Pack(None, ListOfInteger),
            (int)Bls12_381_G1_MultiScalarMul => Pack(ListOfInteger, ListOfG1),
            (int)Bls12_381_G2_MultiScalarMul => Pack(ListOfInteger, ListOfG2),
            _ => None
        };
    }

    private static bool InBounds(int kind, CekValue arg) => kind switch
    {
        CInteger => arg is not VConstant { Value: IntegerConstant n } || n.Value.GetBitLength() <= 262_143,
        CByteString => arg is not VConstant { Value: ByteStringConstant b } || b.Value.Length <= 65_536,
        Int64 => arg is not VConstant { Value: IntegerConstant n } || n.Value.GetBitLength() <= 63,
        Word64 => arg is not VConstant { Value: IntegerConstant n } || (n.Value.Sign >= 0 && n.Value.GetBitLength() <= 64),
        Word8 => arg is not VConstant { Value: IntegerConstant n } || (n.Value.Sign >= 0 && n.Value.GetBitLength() <= 8),
        >= ListOfData => arg is not VConstant { Value: ListConstant l } || l.ItemType == ListItemTypes[kind - ListOfData],
        _ => true
    };

    private static string Message(int kind) => kind switch
    {
        CInteger => "Integer out of bounds",
        CByteString => "ByteString overflow",
        Int64 => "Integer is not within the bounds of Int64",
        Word64 => "Integer is not within the bounds of Word64",
        Word8 => "Integer is not within the bounds of Word8",
        _ => "Type mismatch"
    };
}
