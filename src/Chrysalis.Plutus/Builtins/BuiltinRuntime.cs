using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// Dispatches builtin function calls to their implementations via indexed delegate array.
/// </summary>
internal static class BuiltinRuntime
{
    private static readonly Func<CekValue[], CekValue>[] Dispatchers = BuildDispatchers();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CekValue Call(DefaultFunction function, CekValue[] args) => Dispatchers[(int)function](args);

    private static Func<CekValue[], CekValue>[] BuildDispatchers()
    {
        Func<CekValue[], CekValue>[] d = new Func<CekValue[], CekValue>[101];

        // Integer arithmetic
        d[(int)DefaultFunction.AddInteger] = IntegerBuiltins.AddInteger;
        d[(int)DefaultFunction.SubtractInteger] = IntegerBuiltins.SubtractInteger;
        d[(int)DefaultFunction.MultiplyInteger] = IntegerBuiltins.MultiplyInteger;
        d[(int)DefaultFunction.DivideInteger] = IntegerBuiltins.DivideInteger;
        d[(int)DefaultFunction.QuotientInteger] = IntegerBuiltins.QuotientInteger;
        d[(int)DefaultFunction.RemainderInteger] = IntegerBuiltins.RemainderInteger;
        d[(int)DefaultFunction.ModInteger] = IntegerBuiltins.ModInteger;
        d[(int)DefaultFunction.EqualsInteger] = IntegerBuiltins.EqualsInteger;
        d[(int)DefaultFunction.LessThanInteger] = IntegerBuiltins.LessThanInteger;
        d[(int)DefaultFunction.LessThanEqualsInteger] = IntegerBuiltins.LessThanEqualsInteger;
        d[(int)DefaultFunction.ExpModInteger] = IntegerBuiltins.ExpModInteger;

        // ByteString operations
        d[(int)DefaultFunction.AppendByteString] = ByteStringBuiltins.AppendByteString;
        d[(int)DefaultFunction.ConsByteString] = ByteStringBuiltins.ConsByteString;
        d[(int)DefaultFunction.SliceByteString] = ByteStringBuiltins.SliceByteString;
        d[(int)DefaultFunction.LengthOfByteString] = ByteStringBuiltins.LengthOfByteString;
        d[(int)DefaultFunction.IndexByteString] = ByteStringBuiltins.IndexByteString;
        d[(int)DefaultFunction.EqualsByteString] = ByteStringBuiltins.EqualsByteString;
        d[(int)DefaultFunction.LessThanByteString] = ByteStringBuiltins.LessThanByteString;
        d[(int)DefaultFunction.LessThanEqualsByteString] = ByteStringBuiltins.LessThanEqualsByteString;

        // String operations
        d[(int)DefaultFunction.AppendString] = StringBuiltins.AppendString;
        d[(int)DefaultFunction.EqualsString] = StringBuiltins.EqualsString;
        d[(int)DefaultFunction.EncodeUtf8] = StringBuiltins.EncodeUtf8;
        d[(int)DefaultFunction.DecodeUtf8] = StringBuiltins.DecodeUtf8;

        // Crypto — hash functions
        d[(int)DefaultFunction.Sha2_256] = CryptoBuiltins.Sha2_256;
        d[(int)DefaultFunction.Sha3_256] = CryptoBuiltins.Sha3_256;
        d[(int)DefaultFunction.Blake2b_256] = CryptoBuiltins.Blake2b_256;
        d[(int)DefaultFunction.Blake2b_224] = CryptoBuiltins.Blake2b_224;
        d[(int)DefaultFunction.Keccak_256] = CryptoBuiltins.Keccak_256;
        d[(int)DefaultFunction.Ripemd_160] = CryptoBuiltins.Ripemd_160;

        // Crypto — signature verification
        d[(int)DefaultFunction.VerifyEd25519Signature] = CryptoBuiltins.VerifyEd25519Signature;
        d[(int)DefaultFunction.VerifyEcdsaSecp256k1Signature] = CryptoBuiltins.VerifyEcdsaSecp256k1Signature;
        d[(int)DefaultFunction.VerifySchnorrSecp256k1Signature] = CryptoBuiltins.VerifySchnorrSecp256k1Signature;

        // Control flow
        d[(int)DefaultFunction.IfThenElse] = ControlBuiltins.IfThenElse;
        d[(int)DefaultFunction.ChooseUnit] = ControlBuiltins.ChooseUnit;
        d[(int)DefaultFunction.Trace] = ControlBuiltins.Trace;

        // Pair operations
        d[(int)DefaultFunction.FstPair] = PairBuiltins.FstPair;
        d[(int)DefaultFunction.SndPair] = PairBuiltins.SndPair;

        // List operations
        d[(int)DefaultFunction.ChooseList] = ListBuiltins.ChooseList;
        d[(int)DefaultFunction.MkCons] = ListBuiltins.MkCons;
        d[(int)DefaultFunction.HeadList] = ListBuiltins.HeadList;
        d[(int)DefaultFunction.TailList] = ListBuiltins.TailList;
        d[(int)DefaultFunction.NullList] = ListBuiltins.NullList;
        d[(int)DefaultFunction.DropList] = ListBuiltins.DropList;

        // Data operations
        d[(int)DefaultFunction.ChooseData] = DataBuiltins.ChooseData;
        d[(int)DefaultFunction.ConstrData] = DataBuiltins.ConstrData;
        d[(int)DefaultFunction.MapData] = DataBuiltins.MapData;
        d[(int)DefaultFunction.ListData] = DataBuiltins.ListData;
        d[(int)DefaultFunction.IData] = DataBuiltins.IData;
        d[(int)DefaultFunction.BData] = DataBuiltins.BData;
        d[(int)DefaultFunction.UnConstrData] = DataBuiltins.UnConstrData;
        d[(int)DefaultFunction.UnMapData] = DataBuiltins.UnMapData;
        d[(int)DefaultFunction.UnListData] = DataBuiltins.UnListData;
        d[(int)DefaultFunction.UnIData] = DataBuiltins.UnIData;
        d[(int)DefaultFunction.UnBData] = DataBuiltins.UnBData;
        d[(int)DefaultFunction.EqualsData] = DataBuiltins.EqualsData;
        d[(int)DefaultFunction.MkPairData] = DataBuiltins.MkPairData;
        d[(int)DefaultFunction.MkNilData] = DataBuiltins.MkNilData;
        d[(int)DefaultFunction.MkNilPairData] = DataBuiltins.MkNilPairData;
        d[(int)DefaultFunction.SerialiseData] = DataBuiltins.SerialiseData;

        // BLS12-381 G1
        d[(int)DefaultFunction.Bls12_381_G1_Add] = BlsBuiltins.Bls12_381_G1_Add;
        d[(int)DefaultFunction.Bls12_381_G1_Neg] = BlsBuiltins.Bls12_381_G1_Neg;
        d[(int)DefaultFunction.Bls12_381_G1_ScalarMul] = BlsBuiltins.Bls12_381_G1_ScalarMul;
        d[(int)DefaultFunction.Bls12_381_G1_Equal] = BlsBuiltins.Bls12_381_G1_Equal;
        d[(int)DefaultFunction.Bls12_381_G1_HashToGroup] = BlsBuiltins.Bls12_381_G1_HashToGroup;
        d[(int)DefaultFunction.Bls12_381_G1_Compress] = BlsBuiltins.Bls12_381_G1_Compress;
        d[(int)DefaultFunction.Bls12_381_G1_Uncompress] = BlsBuiltins.Bls12_381_G1_Uncompress;
        d[(int)DefaultFunction.Bls12_381_G1_MultiScalarMul] = BlsBuiltins.Bls12_381_G1_MultiScalarMul;

        // BLS12-381 G2
        d[(int)DefaultFunction.Bls12_381_G2_Add] = BlsBuiltins.Bls12_381_G2_Add;
        d[(int)DefaultFunction.Bls12_381_G2_Neg] = BlsBuiltins.Bls12_381_G2_Neg;
        d[(int)DefaultFunction.Bls12_381_G2_ScalarMul] = BlsBuiltins.Bls12_381_G2_ScalarMul;
        d[(int)DefaultFunction.Bls12_381_G2_Equal] = BlsBuiltins.Bls12_381_G2_Equal;
        d[(int)DefaultFunction.Bls12_381_G2_HashToGroup] = BlsBuiltins.Bls12_381_G2_HashToGroup;
        d[(int)DefaultFunction.Bls12_381_G2_Compress] = BlsBuiltins.Bls12_381_G2_Compress;
        d[(int)DefaultFunction.Bls12_381_G2_Uncompress] = BlsBuiltins.Bls12_381_G2_Uncompress;
        d[(int)DefaultFunction.Bls12_381_G2_MultiScalarMul] = BlsBuiltins.Bls12_381_G2_MultiScalarMul;

        // BLS12-381 pairing
        d[(int)DefaultFunction.Bls12_381_MillerLoop] = BlsBuiltins.Bls12_381_MillerLoop;
        d[(int)DefaultFunction.Bls12_381_MulMlResult] = BlsBuiltins.Bls12_381_MulMlResult;
        d[(int)DefaultFunction.Bls12_381_FinalVerify] = BlsBuiltins.Bls12_381_FinalVerify;

        // Bitwise operations
        d[(int)DefaultFunction.AndByteString] = BitwiseBuiltins.AndByteString;
        d[(int)DefaultFunction.OrByteString] = BitwiseBuiltins.OrByteString;
        d[(int)DefaultFunction.XorByteString] = BitwiseBuiltins.XorByteString;
        d[(int)DefaultFunction.ComplementByteString] = BitwiseBuiltins.ComplementByteString;
        d[(int)DefaultFunction.ReadBit] = BitwiseBuiltins.ReadBit;
        d[(int)DefaultFunction.WriteBits] = BitwiseBuiltins.WriteBits;
        d[(int)DefaultFunction.ReplicateByte] = BitwiseBuiltins.ReplicateByte;
        d[(int)DefaultFunction.ShiftByteString] = BitwiseBuiltins.ShiftByteString;
        d[(int)DefaultFunction.RotateByteString] = BitwiseBuiltins.RotateByteString;
        d[(int)DefaultFunction.CountSetBits] = BitwiseBuiltins.CountSetBits;
        d[(int)DefaultFunction.FindFirstSetBit] = BitwiseBuiltins.FindFirstSetBit;

        // Conversion
        d[(int)DefaultFunction.IntegerToByteString] = BitwiseBuiltins.IntegerToByteString;
        d[(int)DefaultFunction.ByteStringToInteger] = BitwiseBuiltins.ByteStringToInteger;

        // Array operations
        d[(int)DefaultFunction.ListToArray] = ArrayBuiltins.ListToArray;
        d[(int)DefaultFunction.LengthOfArray] = ArrayBuiltins.LengthOfArray;
        d[(int)DefaultFunction.IndexArray] = ArrayBuiltins.IndexArray;

        // Value operations
        d[(int)DefaultFunction.InsertCoin] = ValueBuiltins.InsertCoin;
        d[(int)DefaultFunction.LookupCoin] = ValueBuiltins.LookupCoin;
        d[(int)DefaultFunction.UnionValue] = ValueBuiltins.UnionValue;
        d[(int)DefaultFunction.ValueContains] = ValueBuiltins.ValueContains;
        d[(int)DefaultFunction.ValueData] = ValueBuiltins.ValueData;
        d[(int)DefaultFunction.UnValueData] = ValueBuiltins.UnValueData;
        d[(int)DefaultFunction.ScaleValue] = ValueBuiltins.ScaleValue;

        return d;
    }
}
