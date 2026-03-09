using System.Collections.Immutable;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Builtins;

/// <summary>
/// Dispatches builtin function calls to their implementations.
/// </summary>
internal static class BuiltinRuntime
{
    internal static CekValue Call(DefaultFunction function, ImmutableArray<CekValue> args) =>
        function switch
        {
            // Integer arithmetic
            DefaultFunction.AddInteger => IntegerBuiltins.AddInteger(args),
            DefaultFunction.SubtractInteger => IntegerBuiltins.SubtractInteger(args),
            DefaultFunction.MultiplyInteger => IntegerBuiltins.MultiplyInteger(args),
            DefaultFunction.DivideInteger => IntegerBuiltins.DivideInteger(args),
            DefaultFunction.QuotientInteger => IntegerBuiltins.QuotientInteger(args),
            DefaultFunction.RemainderInteger => IntegerBuiltins.RemainderInteger(args),
            DefaultFunction.ModInteger => IntegerBuiltins.ModInteger(args),
            DefaultFunction.EqualsInteger => IntegerBuiltins.EqualsInteger(args),
            DefaultFunction.LessThanInteger => IntegerBuiltins.LessThanInteger(args),
            DefaultFunction.LessThanEqualsInteger => IntegerBuiltins.LessThanEqualsInteger(args),
            DefaultFunction.ExpModInteger => IntegerBuiltins.ExpModInteger(args),

            // ByteString operations
            DefaultFunction.AppendByteString => ByteStringBuiltins.AppendByteString(args),
            DefaultFunction.ConsByteString => ByteStringBuiltins.ConsByteString(args),
            DefaultFunction.SliceByteString => ByteStringBuiltins.SliceByteString(args),
            DefaultFunction.LengthOfByteString => ByteStringBuiltins.LengthOfByteString(args),
            DefaultFunction.IndexByteString => ByteStringBuiltins.IndexByteString(args),
            DefaultFunction.EqualsByteString => ByteStringBuiltins.EqualsByteString(args),
            DefaultFunction.LessThanByteString => ByteStringBuiltins.LessThanByteString(args),
            DefaultFunction.LessThanEqualsByteString => ByteStringBuiltins.LessThanEqualsByteString(args),

            // String operations
            DefaultFunction.AppendString => StringBuiltins.AppendString(args),
            DefaultFunction.EqualsString => StringBuiltins.EqualsString(args),
            DefaultFunction.EncodeUtf8 => StringBuiltins.EncodeUtf8(args),
            DefaultFunction.DecodeUtf8 => StringBuiltins.DecodeUtf8(args),

            // Crypto — hash functions
            DefaultFunction.Sha2_256 => CryptoBuiltins.Sha2_256(args),
            DefaultFunction.Sha3_256 => CryptoBuiltins.Sha3_256(args),
            DefaultFunction.Blake2b_256 => CryptoBuiltins.Blake2b_256(args),
            DefaultFunction.Blake2b_224 => CryptoBuiltins.Blake2b_224(args),
            DefaultFunction.Keccak_256 => CryptoBuiltins.Keccak_256(args),
            DefaultFunction.Ripemd_160 => CryptoBuiltins.Ripemd_160(args),

            // Crypto — signature verification
            DefaultFunction.VerifyEd25519Signature => CryptoBuiltins.VerifyEd25519Signature(args),
            DefaultFunction.VerifyEcdsaSecp256k1Signature => CryptoBuiltins.VerifyEcdsaSecp256k1Signature(args),
            DefaultFunction.VerifySchnorrSecp256k1Signature => CryptoBuiltins.VerifySchnorrSecp256k1Signature(args),

            // Control flow
            DefaultFunction.IfThenElse => ControlBuiltins.IfThenElse(args),
            DefaultFunction.ChooseUnit => ControlBuiltins.ChooseUnit(args),
            DefaultFunction.Trace => ControlBuiltins.Trace(args),

            // Pair operations
            DefaultFunction.FstPair => PairBuiltins.FstPair(args),
            DefaultFunction.SndPair => PairBuiltins.SndPair(args),

            // List operations
            DefaultFunction.ChooseList => ListBuiltins.ChooseList(args),
            DefaultFunction.MkCons => ListBuiltins.MkCons(args),
            DefaultFunction.HeadList => ListBuiltins.HeadList(args),
            DefaultFunction.TailList => ListBuiltins.TailList(args),
            DefaultFunction.NullList => ListBuiltins.NullList(args),
            DefaultFunction.DropList => ListBuiltins.DropList(args),

            // Data operations
            DefaultFunction.ChooseData => DataBuiltins.ChooseData(args),
            DefaultFunction.ConstrData => DataBuiltins.ConstrData(args),
            DefaultFunction.MapData => DataBuiltins.MapData(args),
            DefaultFunction.ListData => DataBuiltins.ListData(args),
            DefaultFunction.IData => DataBuiltins.IData(args),
            DefaultFunction.BData => DataBuiltins.BData(args),
            DefaultFunction.UnConstrData => DataBuiltins.UnConstrData(args),
            DefaultFunction.UnMapData => DataBuiltins.UnMapData(args),
            DefaultFunction.UnListData => DataBuiltins.UnListData(args),
            DefaultFunction.UnIData => DataBuiltins.UnIData(args),
            DefaultFunction.UnBData => DataBuiltins.UnBData(args),
            DefaultFunction.EqualsData => DataBuiltins.EqualsData(args),
            DefaultFunction.MkPairData => DataBuiltins.MkPairData(args),
            DefaultFunction.MkNilData => DataBuiltins.MkNilData(args),
            DefaultFunction.MkNilPairData => DataBuiltins.MkNilPairData(args),
            DefaultFunction.SerialiseData => DataBuiltins.SerialiseData(args),

            // BLS12-381 G1
            DefaultFunction.Bls12_381_G1_Add => BlsBuiltins.Bls12_381_G1_Add(args),
            DefaultFunction.Bls12_381_G1_Neg => BlsBuiltins.Bls12_381_G1_Neg(args),
            DefaultFunction.Bls12_381_G1_ScalarMul => BlsBuiltins.Bls12_381_G1_ScalarMul(args),
            DefaultFunction.Bls12_381_G1_Equal => BlsBuiltins.Bls12_381_G1_Equal(args),
            DefaultFunction.Bls12_381_G1_HashToGroup => BlsBuiltins.Bls12_381_G1_HashToGroup(args),
            DefaultFunction.Bls12_381_G1_Compress => BlsBuiltins.Bls12_381_G1_Compress(args),
            DefaultFunction.Bls12_381_G1_Uncompress => BlsBuiltins.Bls12_381_G1_Uncompress(args),
            DefaultFunction.Bls12_381_G1_MultiScalarMul => BlsBuiltins.Bls12_381_G1_MultiScalarMul(args),

            // BLS12-381 G2
            DefaultFunction.Bls12_381_G2_Add => BlsBuiltins.Bls12_381_G2_Add(args),
            DefaultFunction.Bls12_381_G2_Neg => BlsBuiltins.Bls12_381_G2_Neg(args),
            DefaultFunction.Bls12_381_G2_ScalarMul => BlsBuiltins.Bls12_381_G2_ScalarMul(args),
            DefaultFunction.Bls12_381_G2_Equal => BlsBuiltins.Bls12_381_G2_Equal(args),
            DefaultFunction.Bls12_381_G2_HashToGroup => BlsBuiltins.Bls12_381_G2_HashToGroup(args),
            DefaultFunction.Bls12_381_G2_Compress => BlsBuiltins.Bls12_381_G2_Compress(args),
            DefaultFunction.Bls12_381_G2_Uncompress => BlsBuiltins.Bls12_381_G2_Uncompress(args),
            DefaultFunction.Bls12_381_G2_MultiScalarMul => BlsBuiltins.Bls12_381_G2_MultiScalarMul(args),

            // BLS12-381 pairing
            DefaultFunction.Bls12_381_MillerLoop => BlsBuiltins.Bls12_381_MillerLoop(args),
            DefaultFunction.Bls12_381_MulMlResult => BlsBuiltins.Bls12_381_MulMlResult(args),
            DefaultFunction.Bls12_381_FinalVerify => BlsBuiltins.Bls12_381_FinalVerify(args),

            // Bitwise operations
            DefaultFunction.AndByteString => BitwiseBuiltins.AndByteString(args),
            DefaultFunction.OrByteString => BitwiseBuiltins.OrByteString(args),
            DefaultFunction.XorByteString => BitwiseBuiltins.XorByteString(args),
            DefaultFunction.ComplementByteString => BitwiseBuiltins.ComplementByteString(args),
            DefaultFunction.ReadBit => BitwiseBuiltins.ReadBit(args),
            DefaultFunction.WriteBits => BitwiseBuiltins.WriteBits(args),
            DefaultFunction.ReplicateByte => BitwiseBuiltins.ReplicateByte(args),
            DefaultFunction.ShiftByteString => BitwiseBuiltins.ShiftByteString(args),
            DefaultFunction.RotateByteString => BitwiseBuiltins.RotateByteString(args),
            DefaultFunction.CountSetBits => BitwiseBuiltins.CountSetBits(args),
            DefaultFunction.FindFirstSetBit => BitwiseBuiltins.FindFirstSetBit(args),

            // Conversion
            DefaultFunction.IntegerToByteString => BitwiseBuiltins.IntegerToByteString(args),
            DefaultFunction.ByteStringToInteger => BitwiseBuiltins.ByteStringToInteger(args),

            // Array operations
            DefaultFunction.ListToArray => ArrayBuiltins.ListToArray(args),
            DefaultFunction.LengthOfArray => ArrayBuiltins.LengthOfArray(args),
            DefaultFunction.IndexArray => ArrayBuiltins.IndexArray(args),

            // Value operations
            DefaultFunction.InsertCoin => ValueBuiltins.InsertCoin(args),
            DefaultFunction.LookupCoin => ValueBuiltins.LookupCoin(args),
            DefaultFunction.UnionValue => ValueBuiltins.UnionValue(args),
            DefaultFunction.ValueContains => ValueBuiltins.ValueContains(args),
            DefaultFunction.ValueData => ValueBuiltins.ValueData(args),
            DefaultFunction.UnValueData => ValueBuiltins.UnValueData(args),
            DefaultFunction.ScaleValue => ValueBuiltins.ScaleValue(args),

            _ => throw new EvaluationException($"builtin not implemented: {function}")
        };
}
