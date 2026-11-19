using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

// One shape table for the reference and chain models. Names and order come from ParamName.
internal static class CostModelLoader
{
    internal static CostModel FromFlat(IReadOnlyList<long> costs, int plutusVersion) => plutusVersion switch
    {
        1 => Build(costs, ParamNames.V1, Variant.D),
        2 => Build(costs, ParamNames.V2, Variant.D),
        _ => Build(costs, ParamNames.V3, Variant.E)
    };

    internal static CostModel Build(IReadOnlyList<long> costs, ImmutableArray<string> names, Variant variant)
    {
        Dictionary<string, int> positions = new(names.Length, StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            positions.Add(names[i], i);
        }

        // The ledger pads short arrays with maxBound and ignores extra values.
        int length = costs.Count;
        long G(string name)
        {
            int i = positions[name];
            return i < length ? costs[i] : long.MaxValue;
        }

        BuiltinCostModel[] c = new BuiltinCostModel[(int)DefaultFunction.ScaleValue + 1];

        // ===== Integer operations =====
        c[(int)DefaultFunction.AddInteger] = new(
            new MaxSizeCost(G("addInteger-cpu-arguments-intercept"), G("addInteger-cpu-arguments-slope")),
            new MaxSizeCost(G("addInteger-memory-arguments-intercept"), G("addInteger-memory-arguments-slope")));

        c[(int)DefaultFunction.SubtractInteger] = new(
            new MaxSizeCost(G("subtractInteger-cpu-arguments-intercept"), G("subtractInteger-cpu-arguments-slope")),
            new MaxSizeCost(G("subtractInteger-memory-arguments-intercept"), G("subtractInteger-memory-arguments-slope")));

        c[(int)DefaultFunction.MultiplyInteger] = new(
            new MultipliedSizesCost(G("multiplyInteger-cpu-arguments-intercept"), G("multiplyInteger-cpu-arguments-slope")),
            new AddedSizesCost(G("multiplyInteger-memory-arguments-intercept"), G("multiplyInteger-memory-arguments-slope")));

        c[(int)DefaultFunction.DivideInteger] = new(DivisionCpu("divideInteger", true), DifferenceMemory("divideInteger"));
        c[(int)DefaultFunction.QuotientInteger] = new(DivisionCpu("quotientInteger", false), DifferenceMemory("quotientInteger"));
        c[(int)DefaultFunction.RemainderInteger] = new(DivisionCpu("remainderInteger", false), LinearCost.InY(
            G("remainderInteger-memory-arguments-intercept"), G("remainderInteger-memory-arguments-slope")));
        c[(int)DefaultFunction.ModInteger] = new(DivisionCpu("modInteger", true), LinearCost.InY(
            G("modInteger-memory-arguments-intercept"), G("modInteger-memory-arguments-slope")));

        c[(int)DefaultFunction.EqualsInteger] = new(
            new MinSizeCost(G("equalsInteger-cpu-arguments-intercept"), G("equalsInteger-cpu-arguments-slope")),
            new ConstantCost(G("equalsInteger-memory-arguments")));

        c[(int)DefaultFunction.LessThanInteger] = new(
            new MinSizeCost(G("lessThanInteger-cpu-arguments-intercept"), G("lessThanInteger-cpu-arguments-slope")),
            new ConstantCost(G("lessThanInteger-memory-arguments")));

        c[(int)DefaultFunction.LessThanEqualsInteger] = new(
            new MinSizeCost(G("lessThanEqualsInteger-cpu-arguments-intercept"), G("lessThanEqualsInteger-cpu-arguments-slope")),
            new ConstantCost(G("lessThanEqualsInteger-memory-arguments")));

        // ===== ByteString operations =====
        c[(int)DefaultFunction.AppendByteString] = new(
            new AddedSizesCost(G("appendByteString-cpu-arguments-intercept"), G("appendByteString-cpu-arguments-slope")),
            new AddedSizesCost(G("appendByteString-memory-arguments-intercept"), G("appendByteString-memory-arguments-slope")));

        c[(int)DefaultFunction.ConsByteString] = new(
            LinearCost.InY(G("consByteString-cpu-arguments-intercept"), G("consByteString-cpu-arguments-slope")),
            new AddedSizesCost(G("consByteString-memory-arguments-intercept"), G("consByteString-memory-arguments-slope")));

        c[(int)DefaultFunction.SliceByteString] = new(
            LinearCost.InZ(G("sliceByteString-cpu-arguments-intercept"), G("sliceByteString-cpu-arguments-slope")),
            LinearCost.InZ(G("sliceByteString-memory-arguments-intercept"), G("sliceByteString-memory-arguments-slope")));

        c[(int)DefaultFunction.LengthOfByteString] = new(
            new ConstantCost(G("lengthOfByteString-cpu-arguments")),
            new ConstantCost(G("lengthOfByteString-memory-arguments")));

        c[(int)DefaultFunction.IndexByteString] = new(
            new ConstantCost(G("indexByteString-cpu-arguments")),
            new ConstantCost(G("indexByteString-memory-arguments")));

        c[(int)DefaultFunction.EqualsByteString] = new(
            new LinearOnDiagonalCost(
                G("equalsByteString-cpu-arguments-intercept"), G("equalsByteString-cpu-arguments-slope"),
                G("equalsByteString-cpu-arguments-constant")),
            new ConstantCost(G("equalsByteString-memory-arguments")));

        c[(int)DefaultFunction.LessThanByteString] = new(
            new MinSizeCost(G("lessThanByteString-cpu-arguments-intercept"), G("lessThanByteString-cpu-arguments-slope")),
            new ConstantCost(G("lessThanByteString-memory-arguments")));

        c[(int)DefaultFunction.LessThanEqualsByteString] = new(
            new MinSizeCost(G("lessThanEqualsByteString-cpu-arguments-intercept"), G("lessThanEqualsByteString-cpu-arguments-slope")),
            new ConstantCost(G("lessThanEqualsByteString-memory-arguments")));

        // ===== Cryptography and hash functions =====
        c[(int)DefaultFunction.Sha2_256] = new(
            LinearCost.InX(G("sha2_256-cpu-arguments-intercept"), G("sha2_256-cpu-arguments-slope")),
            new ConstantCost(G("sha2_256-memory-arguments")));

        c[(int)DefaultFunction.Sha3_256] = new(
            LinearCost.InX(G("sha3_256-cpu-arguments-intercept"), G("sha3_256-cpu-arguments-slope")),
            new ConstantCost(G("sha3_256-memory-arguments")));

        c[(int)DefaultFunction.Blake2b_256] = new(
            LinearCost.InX(G("blake2b_256-cpu-arguments-intercept"), G("blake2b_256-cpu-arguments-slope")),
            new ConstantCost(G("blake2b_256-memory-arguments")));

        c[(int)DefaultFunction.Blake2b_224] = new(
            LinearCost.InX(G("blake2b_224-cpu-arguments-intercept"), G("blake2b_224-cpu-arguments-slope")),
            new ConstantCost(G("blake2b_224-memory-arguments")));

        c[(int)DefaultFunction.Keccak_256] = new(
            LinearCost.InX(G("keccak_256-cpu-arguments-intercept"), G("keccak_256-cpu-arguments-slope")),
            new ConstantCost(G("keccak_256-memory-arguments")));

        c[(int)DefaultFunction.VerifyEd25519Signature] = new(
            LinearCost.InY(G("verifyEd25519Signature-cpu-arguments-intercept"), G("verifyEd25519Signature-cpu-arguments-slope")),
            new ConstantCost(G("verifyEd25519Signature-memory-arguments")));

        c[(int)DefaultFunction.VerifyEcdsaSecp256k1Signature] = new(
            new ConstantCost(G("verifyEcdsaSecp256k1Signature-cpu-arguments")),
            new ConstantCost(G("verifyEcdsaSecp256k1Signature-memory-arguments")));

        c[(int)DefaultFunction.VerifySchnorrSecp256k1Signature] = new(
            LinearCost.InY(G("verifySchnorrSecp256k1Signature-cpu-arguments-intercept"), G("verifySchnorrSecp256k1Signature-cpu-arguments-slope")),
            new ConstantCost(G("verifySchnorrSecp256k1Signature-memory-arguments")));

        c[(int)DefaultFunction.Ripemd_160] = new(
            LinearCost.InX(G("ripemd_160-cpu-arguments-intercept"), G("ripemd_160-cpu-arguments-slope")),
            new ConstantCost(G("ripemd_160-memory-arguments")));

        // ===== String operations =====
        c[(int)DefaultFunction.AppendString] = new(
            new AddedSizesCost(G("appendString-cpu-arguments-intercept"), G("appendString-cpu-arguments-slope")),
            new AddedSizesCost(G("appendString-memory-arguments-intercept"), G("appendString-memory-arguments-slope")));

        c[(int)DefaultFunction.EqualsString] = new(
            new LinearOnDiagonalCost(
                G("equalsString-cpu-arguments-intercept"), G("equalsString-cpu-arguments-slope"),
                G("equalsString-cpu-arguments-constant")),
            new ConstantCost(G("equalsString-memory-arguments")));

        c[(int)DefaultFunction.EncodeUtf8] = new(
            LinearCost.InX(G("encodeUtf8-cpu-arguments-intercept"), G("encodeUtf8-cpu-arguments-slope")),
            LinearCost.InX(G("encodeUtf8-memory-arguments-intercept"), G("encodeUtf8-memory-arguments-slope")));

        c[(int)DefaultFunction.DecodeUtf8] = new(
            LinearCost.InX(G("decodeUtf8-cpu-arguments-intercept"), G("decodeUtf8-cpu-arguments-slope")),
            LinearCost.InX(G("decodeUtf8-memory-arguments-intercept"), G("decodeUtf8-memory-arguments-slope")));

        // ===== Control flow =====
        c[(int)DefaultFunction.IfThenElse] = new(
            new ConstantCost(G("ifThenElse-cpu-arguments")), new ConstantCost(G("ifThenElse-memory-arguments")));
        c[(int)DefaultFunction.ChooseUnit] = new(
            new ConstantCost(G("chooseUnit-cpu-arguments")), new ConstantCost(G("chooseUnit-memory-arguments")));
        c[(int)DefaultFunction.Trace] = new(
            new ConstantCost(G("trace-cpu-arguments")), new ConstantCost(G("trace-memory-arguments")));

        // ===== Pair operations =====
        c[(int)DefaultFunction.FstPair] = new(
            new ConstantCost(G("fstPair-cpu-arguments")), new ConstantCost(G("fstPair-memory-arguments")));
        c[(int)DefaultFunction.SndPair] = new(
            new ConstantCost(G("sndPair-cpu-arguments")), new ConstantCost(G("sndPair-memory-arguments")));

        // ===== List operations =====
        c[(int)DefaultFunction.ChooseList] = new(
            new ConstantCost(G("chooseList-cpu-arguments")), new ConstantCost(G("chooseList-memory-arguments")));
        c[(int)DefaultFunction.MkCons] = new(
            new ConstantCost(G("mkCons-cpu-arguments")), new ConstantCost(G("mkCons-memory-arguments")));
        c[(int)DefaultFunction.HeadList] = new(
            new ConstantCost(G("headList-cpu-arguments")), new ConstantCost(G("headList-memory-arguments")));
        c[(int)DefaultFunction.TailList] = new(
            new ConstantCost(G("tailList-cpu-arguments")), new ConstantCost(G("tailList-memory-arguments")));
        c[(int)DefaultFunction.NullList] = new(
            new ConstantCost(G("nullList-cpu-arguments")), new ConstantCost(G("nullList-memory-arguments")));

        // ===== Data operations =====
        c[(int)DefaultFunction.ChooseData] = new(
            new ConstantCost(G("chooseData-cpu-arguments")), new ConstantCost(G("chooseData-memory-arguments")));
        c[(int)DefaultFunction.ConstrData] = new(
            new ConstantCost(G("constrData-cpu-arguments")), new ConstantCost(G("constrData-memory-arguments")));
        c[(int)DefaultFunction.MapData] = new(
            new ConstantCost(G("mapData-cpu-arguments")), new ConstantCost(G("mapData-memory-arguments")));
        c[(int)DefaultFunction.ListData] = new(
            new ConstantCost(G("listData-cpu-arguments")), new ConstantCost(G("listData-memory-arguments")));
        c[(int)DefaultFunction.IData] = new(
            new ConstantCost(G("iData-cpu-arguments")), new ConstantCost(G("iData-memory-arguments")));
        c[(int)DefaultFunction.BData] = new(
            new ConstantCost(G("bData-cpu-arguments")), new ConstantCost(G("bData-memory-arguments")));
        c[(int)DefaultFunction.UnConstrData] = new(
            new ConstantCost(G("unConstrData-cpu-arguments")), new ConstantCost(G("unConstrData-memory-arguments")));
        c[(int)DefaultFunction.UnMapData] = new(
            new ConstantCost(G("unMapData-cpu-arguments")), new ConstantCost(G("unMapData-memory-arguments")));
        c[(int)DefaultFunction.UnListData] = new(
            new ConstantCost(G("unListData-cpu-arguments")), new ConstantCost(G("unListData-memory-arguments")));
        c[(int)DefaultFunction.UnIData] = new(
            new ConstantCost(G("unIData-cpu-arguments")), new ConstantCost(G("unIData-memory-arguments")));
        c[(int)DefaultFunction.UnBData] = new(
            new ConstantCost(G("unBData-cpu-arguments")), new ConstantCost(G("unBData-memory-arguments")));

        c[(int)DefaultFunction.EqualsData] = new(
            new MinSizeCost(G("equalsData-cpu-arguments-intercept"), G("equalsData-cpu-arguments-slope")),
            new ConstantCost(G("equalsData-memory-arguments")));

        c[(int)DefaultFunction.MkPairData] = new(
            new ConstantCost(G("mkPairData-cpu-arguments")), new ConstantCost(G("mkPairData-memory-arguments")));
        c[(int)DefaultFunction.MkNilData] = new(
            new ConstantCost(G("mkNilData-cpu-arguments")), new ConstantCost(G("mkNilData-memory-arguments")));
        c[(int)DefaultFunction.MkNilPairData] = new(
            new ConstantCost(G("mkNilPairData-cpu-arguments")), new ConstantCost(G("mkNilPairData-memory-arguments")));

        c[(int)DefaultFunction.SerialiseData] = new(
            LinearCost.InX(G("serialiseData-cpu-arguments-intercept"), G("serialiseData-cpu-arguments-slope")),
            LinearCost.InX(G("serialiseData-memory-arguments-intercept"), G("serialiseData-memory-arguments-slope")));

        // ===== BLS12-381 G1 =====
        c[(int)DefaultFunction.Bls12_381_G1_Add] = new(
            new ConstantCost(G("bls12_381_G1_add-cpu-arguments")), new ConstantCost(G("bls12_381_G1_add-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Neg] = new(
            new ConstantCost(G("bls12_381_G1_neg-cpu-arguments")), new ConstantCost(G("bls12_381_G1_neg-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_ScalarMul] = new(
            LinearCost.InX(G("bls12_381_G1_scalarMul-cpu-arguments-intercept"), G("bls12_381_G1_scalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G1_scalarMul-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Equal] = new(
            new ConstantCost(G("bls12_381_G1_equal-cpu-arguments")), new ConstantCost(G("bls12_381_G1_equal-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Compress] = new(
            new ConstantCost(G("bls12_381_G1_compress-cpu-arguments")), new ConstantCost(G("bls12_381_G1_compress-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Uncompress] = new(
            new ConstantCost(G("bls12_381_G1_uncompress-cpu-arguments")), new ConstantCost(G("bls12_381_G1_uncompress-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_HashToGroup] = new(
            LinearCost.InX(G("bls12_381_G1_hashToGroup-cpu-arguments-intercept"), G("bls12_381_G1_hashToGroup-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G1_hashToGroup-memory-arguments")));

        // ===== BLS12-381 G2 =====
        c[(int)DefaultFunction.Bls12_381_G2_Add] = new(
            new ConstantCost(G("bls12_381_G2_add-cpu-arguments")), new ConstantCost(G("bls12_381_G2_add-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Neg] = new(
            new ConstantCost(G("bls12_381_G2_neg-cpu-arguments")), new ConstantCost(G("bls12_381_G2_neg-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_ScalarMul] = new(
            LinearCost.InX(G("bls12_381_G2_scalarMul-cpu-arguments-intercept"), G("bls12_381_G2_scalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G2_scalarMul-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Equal] = new(
            new ConstantCost(G("bls12_381_G2_equal-cpu-arguments")), new ConstantCost(G("bls12_381_G2_equal-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Compress] = new(
            new ConstantCost(G("bls12_381_G2_compress-cpu-arguments")), new ConstantCost(G("bls12_381_G2_compress-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Uncompress] = new(
            new ConstantCost(G("bls12_381_G2_uncompress-cpu-arguments")), new ConstantCost(G("bls12_381_G2_uncompress-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_HashToGroup] = new(
            LinearCost.InX(G("bls12_381_G2_hashToGroup-cpu-arguments-intercept"), G("bls12_381_G2_hashToGroup-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G2_hashToGroup-memory-arguments")));

        // ===== BLS12-381 pairing =====
        c[(int)DefaultFunction.Bls12_381_MillerLoop] = new(
            new ConstantCost(G("bls12_381_millerLoop-cpu-arguments")), new ConstantCost(G("bls12_381_millerLoop-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_MulMlResult] = new(
            new ConstantCost(G("bls12_381_mulMlResult-cpu-arguments")), new ConstantCost(G("bls12_381_mulMlResult-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_FinalVerify] = new(
            new ConstantCost(G("bls12_381_finalVerify-cpu-arguments")), new ConstantCost(G("bls12_381_finalVerify-memory-arguments")));

        // ===== Byte/Integer conversion =====
        c[(int)DefaultFunction.IntegerToByteString] = new(
            QuadraticCost.InZ(
                G("integerToByteString-cpu-arguments-c0"), G("integerToByteString-cpu-arguments-c1"),
                G("integerToByteString-cpu-arguments-c2")),
            new LiteralInYOrLinearInZCost(
                G("integerToByteString-memory-arguments-intercept"), G("integerToByteString-memory-arguments-slope")));

        c[(int)DefaultFunction.ByteStringToInteger] = new(
            QuadraticCost.InY(
                G("byteStringToInteger-cpu-arguments-c0"), G("byteStringToInteger-cpu-arguments-c1"),
                G("byteStringToInteger-cpu-arguments-c2")),
            LinearCost.InY(
                G("byteStringToInteger-memory-arguments-intercept"), G("byteStringToInteger-memory-arguments-slope")));

        // ===== Bitwise operations (present when Count >= 297, guaranteed above) =====
        c[(int)DefaultFunction.AndByteString] = new(
            new LinearInYAndZCost(G("andByteString-cpu-arguments-intercept"), G("andByteString-cpu-arguments-slope1"), G("andByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("andByteString-memory-arguments-intercept"), G("andByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.OrByteString] = new(
            new LinearInYAndZCost(G("orByteString-cpu-arguments-intercept"), G("orByteString-cpu-arguments-slope1"), G("orByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("orByteString-memory-arguments-intercept"), G("orByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.XorByteString] = new(
            new LinearInYAndZCost(G("xorByteString-cpu-arguments-intercept"), G("xorByteString-cpu-arguments-slope1"), G("xorByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("xorByteString-memory-arguments-intercept"), G("xorByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.ComplementByteString] = new(
            LinearCost.InX(G("complementByteString-cpu-arguments-intercept"), G("complementByteString-cpu-arguments-slope")),
            LinearCost.InX(G("complementByteString-memory-arguments-intercept"), G("complementByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.ReadBit] = new(
            new ConstantCost(G("readBit-cpu-arguments")), new ConstantCost(G("readBit-memory-arguments")));
        c[(int)DefaultFunction.WriteBits] = new(
            LinearCost.InY(G("writeBits-cpu-arguments-intercept"), G("writeBits-cpu-arguments-slope")),
            LinearCost.InX(G("writeBits-memory-arguments-intercept"), G("writeBits-memory-arguments-slope")));
        c[(int)DefaultFunction.ReplicateByte] = new(
            LinearCost.InX(G("replicateByte-cpu-arguments-intercept"), G("replicateByte-cpu-arguments-slope")),
            LinearCost.InX(G("replicateByte-memory-arguments-intercept"), G("replicateByte-memory-arguments-slope")));
        c[(int)DefaultFunction.ShiftByteString] = new(
            LinearCost.InX(G("shiftByteString-cpu-arguments-intercept"), G("shiftByteString-cpu-arguments-slope")),
            LinearCost.InX(G("shiftByteString-memory-arguments-intercept"), G("shiftByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.RotateByteString] = new(
            LinearCost.InX(G("rotateByteString-cpu-arguments-intercept"), G("rotateByteString-cpu-arguments-slope")),
            LinearCost.InX(G("rotateByteString-memory-arguments-intercept"), G("rotateByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.CountSetBits] = new(
            LinearCost.InX(G("countSetBits-cpu-arguments-intercept"), G("countSetBits-cpu-arguments-slope")),
            new ConstantCost(G("countSetBits-memory-arguments")));
        c[(int)DefaultFunction.FindFirstSetBit] = new(
            LinearCost.InX(G("findFirstSetBit-cpu-arguments-intercept"), G("findFirstSetBit-cpu-arguments-slope")),
            new ConstantCost(G("findFirstSetBit-memory-arguments")));

        // Batch 6 (PV11, all languages)
        c[(int)DefaultFunction.ExpModInteger] = new(
            new ExpModCost(G("expModInteger-cpu-arguments-coefficient00"), G("expModInteger-cpu-arguments-coefficient11"),
                G("expModInteger-cpu-arguments-coefficient12")),
            LinearCost.InZ(G("expModInteger-memory-arguments-intercept"), G("expModInteger-memory-arguments-slope")));
        c[(int)DefaultFunction.DropList] = new(
            LinearCost.InX(G("dropList-cpu-arguments-intercept"), G("dropList-cpu-arguments-slope")),
            new ConstantCost(G("dropList-memory-arguments")));
        c[(int)DefaultFunction.LengthOfArray] = Constants("lengthOfArray");
        c[(int)DefaultFunction.ListToArray] = new(
            LinearCost.InX(G("listToArray-cpu-arguments-intercept"), G("listToArray-cpu-arguments-slope")),
            LinearCost.InX(G("listToArray-memory-arguments-intercept"), G("listToArray-memory-arguments-slope")));
        c[(int)DefaultFunction.IndexArray] = Constants("indexArray");
        c[(int)DefaultFunction.Bls12_381_G1_MultiScalarMul] = new(
            LinearCost.InX(G("bls12_381_G1_multiScalarMul-cpu-arguments-intercept"), G("bls12_381_G1_multiScalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G1_multiScalarMul-memory-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_MultiScalarMul] = new(
            LinearCost.InX(G("bls12_381_G2_multiScalarMul-cpu-arguments-intercept"), G("bls12_381_G2_multiScalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G2_multiScalarMul-memory-arguments")));

        // InsertCoin's Value size is placed first by ExMem (upstream uses z).
        c[(int)DefaultFunction.InsertCoin] = new(
            LinearCost.InX(G("insertCoin-cpu-arguments-intercept"), G("insertCoin-cpu-arguments-slope")),
            LinearCost.InX(G("insertCoin-memory-arguments-intercept"), G("insertCoin-memory-arguments-slope")));
        c[(int)DefaultFunction.LookupCoin] = new(
            LinearCost.InZ(G("lookupCoin-cpu-arguments-intercept"), G("lookupCoin-cpu-arguments-slope")),
            new ConstantCost(G("lookupCoin-memory-arguments")));
        c[(int)DefaultFunction.UnionValue] = new(
            new WithInteractionCost(G("unionValue-cpu-arguments-c00"), G("unionValue-cpu-arguments-c10"),
                G("unionValue-cpu-arguments-c01"), G("unionValue-cpu-arguments-c11")),
            new AddedSizesCost(G("unionValue-memory-arguments-intercept"), G("unionValue-memory-arguments-slope")));
        c[(int)DefaultFunction.ValueContains] = new(
            new ConstAboveDiagonalCost(G("valueContains-cpu-arguments-constant"), new LinearInXAndYCost(
                G("valueContains-cpu-arguments-model-arguments-intercept"),
                G("valueContains-cpu-arguments-model-arguments-slope1"), G("valueContains-cpu-arguments-model-arguments-slope2"))),
            new ConstantCost(G("valueContains-memory-arguments")));
        c[(int)DefaultFunction.ValueData] = new(
            LinearCost.InX(G("valueData-cpu-arguments-intercept"), G("valueData-cpu-arguments-slope")),
            LinearCost.InX(G("valueData-memory-arguments-intercept"), G("valueData-memory-arguments-slope")));
        c[(int)DefaultFunction.UnValueData] = new(
            QuadraticCost.InX(G("unValueData-cpu-arguments-c0"), G("unValueData-cpu-arguments-c1"), G("unValueData-cpu-arguments-c2")),
            LinearCost.InX(G("unValueData-memory-arguments-intercept"), G("unValueData-memory-arguments-slope")));
        c[(int)DefaultFunction.ScaleValue] = new(
            LinearCost.InY(G("scaleValue-cpu-arguments-intercept"), G("scaleValue-cpu-arguments-slope")),
            LinearCost.InY(G("scaleValue-memory-arguments-intercept"), G("scaleValue-memory-arguments-slope")));

        // CEK machine kinds in counter/flush order, independently of their flat array order.
        MachineCosts machine = new(Cek("cekStartupCost"),
        [
            Cek("cekConstCost"), Cek("cekVarCost"), Cek("cekLamCost"), Cek("cekApplyCost"), Cek("cekDelayCost"),
            Cek("cekForceCost"), Cek("cekBuiltinCost"), Cek("cekConstrCost"), Cek("cekCaseCost")
        ]);
        return new(ImmutableCollectionsMarshal.AsImmutableArray(c), machine, variant);

        ExBudget Cek(string kind) => new(G(kind + "-exBudgetCPU"), G(kind + "-exBudgetMemory"));
        BuiltinCostModel Constants(string name) => new(new ConstantCost(G(name + "-cpu-arguments")), new ConstantCost(G(name + "-memory-arguments")));

        CostFunction DivisionCpu(string builtin, bool sortForChain)
        {
            string key = builtin + "-cpu-arguments-";
            string inner = key + "model-arguments-";
            CostFunction model = variant == Variant.D
                ? new MultipliedSizesCost(G(inner + "intercept"), G(inner + "slope"))
                : new QuadraticInXAndYCost(G(inner + "minimum"), G(inner + "c00"), G(inner + "c10"), G(inner + "c01"),
                    G(inner + "c20"), G(inner + "c11"), G(inner + "c02"));
            return sortForChain && variant != Variant.C
                ? new AboveAndBelowDiagonalCost(model)
                : new ConstAboveDiagonalCost(G(key + "constant"), model);
        }

        SubtractedSizesCost DifferenceMemory(string builtin) => new(G(builtin + "-memory-arguments-intercept"),
            G(builtin + "-memory-arguments-slope"), G(builtin + "-memory-arguments-minimum"));
    }
}

internal enum Variant : byte { C, D, E }
