namespace Chrysalis.Plutus.Types;

/// <summary>
/// All built-in functions available in Plutus Core, ordered by their 7-bit Flat encoding tag.
/// Tags are assigned in batches corresponding to ledger language versions.
/// </summary>
public enum DefaultFunction
{
    // Batch 1 — PlutusV1 (Alonzo, protocol version 5.0)
    AddInteger = 0,
    SubtractInteger = 1,
    MultiplyInteger = 2,
    DivideInteger = 3,
    QuotientInteger = 4,
    RemainderInteger = 5,
    ModInteger = 6,
    EqualsInteger = 7,
    LessThanInteger = 8,
    LessThanEqualsInteger = 9,
    AppendByteString = 10,
    ConsByteString = 11,
    SliceByteString = 12,
    LengthOfByteString = 13,
    IndexByteString = 14,
    EqualsByteString = 15,
    LessThanByteString = 16,
    LessThanEqualsByteString = 17,
    Sha2_256 = 18,
    Sha3_256 = 19,
    Blake2b_256 = 20,
    VerifyEd25519Signature = 21,
    AppendString = 22,
    EqualsString = 23,
    EncodeUtf8 = 24,
    DecodeUtf8 = 25,
    IfThenElse = 26,
    ChooseUnit = 27,
    Trace = 28,
    FstPair = 29,
    SndPair = 30,
    ChooseList = 31,
    MkCons = 32,
    HeadList = 33,
    TailList = 34,
    NullList = 35,
    ChooseData = 36,
    ConstrData = 37,
    MapData = 38,
    ListData = 39,
    IData = 40,
    BData = 41,
    UnConstrData = 42,
    UnMapData = 43,
    UnListData = 44,
    UnIData = 45,
    UnBData = 46,
    EqualsData = 47,
    MkPairData = 48,
    MkNilData = 49,
    MkNilPairData = 50,

    // Batch 2 — PlutusV2 (Vasil, protocol version 7.0)
    SerialiseData = 51,

    // Batch 3 — PlutusV3 (secp256k1, protocol version 9.0)
    VerifyEcdsaSecp256k1Signature = 52,
    VerifySchnorrSecp256k1Signature = 53,

    // Batch 4 — PlutusV3 (BLS12-381, conversions)
    Bls12_381_G1_Add = 54,
    Bls12_381_G1_Neg = 55,
    Bls12_381_G1_ScalarMul = 56,
    Bls12_381_G1_Equal = 57,
    Bls12_381_G1_HashToGroup = 58,
    Bls12_381_G1_Compress = 59,
    Bls12_381_G1_Uncompress = 60,
    Bls12_381_G2_Add = 61,
    Bls12_381_G2_Neg = 62,
    Bls12_381_G2_ScalarMul = 63,
    Bls12_381_G2_Equal = 64,
    Bls12_381_G2_HashToGroup = 65,
    Bls12_381_G2_Compress = 66,
    Bls12_381_G2_Uncompress = 67,
    Bls12_381_MillerLoop = 68,
    Bls12_381_MulMlResult = 69,
    Bls12_381_FinalVerify = 70,
    Keccak_256 = 71,
    Blake2b_224 = 72,
    IntegerToByteString = 73,
    ByteStringToInteger = 74,

    // Batch 5 — bitwise operations, RIPEMD-160
    AndByteString = 75,
    OrByteString = 76,
    XorByteString = 77,
    ComplementByteString = 78,
    ReadBit = 79,
    WriteBits = 80,
    ReplicateByte = 81,
    ShiftByteString = 82,
    RotateByteString = 83,
    CountSetBits = 84,
    FindFirstSetBit = 85,
    Ripemd_160 = 86,

    // Batch 6 — arrays, expMod, multiScalarMul, dropList
    ExpModInteger = 87,
    DropList = 88,
    LengthOfArray = 89,
    ListToArray = 90,
    IndexArray = 91,
    Bls12_381_G1_MultiScalarMul = 92,
    Bls12_381_G2_MultiScalarMul = 93,

    // Batch 7 — Value operations (PlutusV4)
    InsertCoin = 94,
    LookupCoin = 95,
    UnionValue = 96,
    ValueContains = 97,
    ValueData = 98,
    UnValueData = 99,
    ScaleValue = 100,
}

public static class DefaultFunctionExtensions
{
    private static readonly byte[] Arities =
    [
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, // 0-9: integer ops
        2, 2, 3, 1, 2, 2, 2, 2,        // 10-17: bytestring ops
        1, 1, 1, 3,                     // 18-21: crypto (sha2, sha3, blake2b, ed25519)
        2, 2, 1, 1,                     // 22-25: string ops
        3, 2, 2,                        // 26-28: ifThenElse, chooseUnit, trace
        1, 1,                           // 29-30: fstPair, sndPair
        3, 2, 1, 1, 1,                 // 31-35: chooseList, mkCons, head, tail, null
        6, 2, 1, 1, 1, 1,             // 36-41: chooseData, constrData, mapData, listData, iData, bData
        1, 1, 1, 1, 1,                 // 42-46: unConstrData..unBData
        2, 2, 1, 1,                     // 47-50: equalsData, mkPairData, mkNilData, mkNilPairData
        1,                              // 51: serialiseData
        3, 3,                           // 52-53: secp256k1 ECDSA, Schnorr
        2, 1, 2, 2, 2, 1, 1,          // 54-60: BLS G1 ops
        2, 1, 2, 2, 2, 1, 1,          // 61-67: BLS G2 ops
        2, 2, 2,                        // 68-70: millerLoop, mulMlResult, finalVerify
        1, 1,                           // 71-72: keccak_256, blake2b_224
        3, 2,                           // 73-74: integerToByteString, byteStringToInteger
        3, 3, 3, 1, 2, 3, 2, 2, 2, 1, 1, // 75-85: bitwise ops
        1,                              // 86: ripemd_160
        3, 2, 1, 1, 2, 2, 2,          // 87-93: expMod, dropList, array ops, multiScalarMul
        4, 3, 2, 2, 1, 1, 2,          // 94-100: insertCoin, lookupCoin, unionValue, valueContains, valueData, unValueData, scaleValue
    ];

    private static readonly byte[] ForceCounts =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0-9: integer ops
        0, 0, 0, 0, 0, 0, 0, 0,        // 10-17: bytestring ops
        0, 0, 0, 0,                     // 18-21: crypto
        0, 0, 0, 0,                     // 22-25: string ops
        1, 1, 1,                        // 26-28: ifThenElse, chooseUnit, trace
        2, 2,                           // 29-30: fstPair, sndPair
        2, 1, 1, 1, 1,                 // 31-35: chooseList, mkCons, head, tail, null
        1, 0, 0, 0, 0, 0,             // 36-41: chooseData, constrData..bData
        0, 0, 0, 0, 0,                 // 42-46: unConstrData..unBData
        0, 0, 0, 0,                     // 47-50: equalsData, mkPairData, mkNilData, mkNilPairData
        0,                              // 51: serialiseData
        0, 0,                           // 52-53: secp256k1
        0, 0, 0, 0, 0, 0, 0,          // 54-60: BLS G1
        0, 0, 0, 0, 0, 0, 0,          // 61-67: BLS G2
        0, 0, 0,                        // 68-70: millerLoop, mulMlResult, finalVerify
        0, 0,                           // 71-72: keccak, blake2b_224
        0, 0,                           // 73-74: integerToByteString, byteStringToInteger
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 75-85: bitwise ops
        0,                              // 86: ripemd_160
        0, 1, 1, 1, 1, 0, 0,          // 87-93: expMod, dropList, array ops, multiScalarMul
        0, 0, 0, 0, 0, 0, 0,          // 94-100: value ops (no forces)
    ];

    public static int Arity(this DefaultFunction func)
    {
        return Arities[(int)func];
    }

    public static int ForceCount(this DefaultFunction func)
    {
        return ForceCounts[(int)func];
    }
}
