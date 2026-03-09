using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Default PlutusV3 (Conway era) builtin cost parameters.
/// Values match Cardano mainnet protocol parameters.
/// </summary>
internal static class DefaultCosts
{
    private static BuiltinCostModel Const(long cpu, long mem) =>
        new(new ConstantCost(cpu), new ConstantCost(mem));

    private static readonly CostFunction DivCpu = new ConstAboveDiagonalCost(
        85848, 85848, 123203, 1716, 7305, 57, 549, -900);

    internal static BuiltinCostModel[] Create()
    {
        BuiltinCostModel[] costs = new BuiltinCostModel[101];

        // ===== Integer operations =====

        costs[(int)DefaultFunction.AddInteger] = new(
            new MaxSizeCost(100788, 420),
            new MaxSizeCost(1, 1));

        costs[(int)DefaultFunction.SubtractInteger] = new(
            new MaxSizeCost(100788, 420),
            new MaxSizeCost(1, 1));

        costs[(int)DefaultFunction.MultiplyInteger] = new(
            new MultipliedSizesCost(90434, 519),
            new AddedSizesCost(0, 1));

        costs[(int)DefaultFunction.DivideInteger] = new(
            DivCpu,
            new SubtractedSizesCost(0, 1, 1));

        costs[(int)DefaultFunction.QuotientInteger] = new(
            DivCpu,
            new SubtractedSizesCost(0, 1, 1));

        costs[(int)DefaultFunction.RemainderInteger] = new(
            DivCpu,
            LinearCost.InY(0, 1));

        costs[(int)DefaultFunction.ModInteger] = new(
            DivCpu,
            LinearCost.InY(0, 1));

        costs[(int)DefaultFunction.EqualsInteger] = new(
            new MinSizeCost(51775, 558),
            new ConstantCost(1));

        costs[(int)DefaultFunction.LessThanInteger] = new(
            new MinSizeCost(44749, 541),
            new ConstantCost(1));

        costs[(int)DefaultFunction.LessThanEqualsInteger] = new(
            new MinSizeCost(43285, 552),
            new ConstantCost(1));

        // ===== ByteString operations =====

        costs[(int)DefaultFunction.AppendByteString] = new(
            new AddedSizesCost(1000, 173),
            new AddedSizesCost(0, 1));

        costs[(int)DefaultFunction.ConsByteString] = new(
            LinearCost.InY(72010, 178),
            new AddedSizesCost(0, 1));

        costs[(int)DefaultFunction.SliceByteString] = new(
            LinearCost.InZ(20467, 1),
            LinearCost.InZ(4, 0));

        costs[(int)DefaultFunction.LengthOfByteString] = Const(22100, 10);
        costs[(int)DefaultFunction.IndexByteString] = Const(13169, 4);

        costs[(int)DefaultFunction.EqualsByteString] = new(
            new LinearOnDiagonalCost(29498, 38, 24548),
            new ConstantCost(1));

        costs[(int)DefaultFunction.LessThanByteString] = new(
            new MinSizeCost(28999, 74),
            new ConstantCost(1));

        costs[(int)DefaultFunction.LessThanEqualsByteString] = new(
            new MinSizeCost(28999, 74),
            new ConstantCost(1));

        // ===== Cryptography and hash functions =====

        costs[(int)DefaultFunction.Sha2_256] = new(
            LinearCost.InX(270652, 22588),
            new ConstantCost(4));

        costs[(int)DefaultFunction.Sha3_256] = new(
            LinearCost.InX(1457325, 64566),
            new ConstantCost(4));

        costs[(int)DefaultFunction.Blake2b_256] = new(
            LinearCost.InX(201305, 8356),
            new ConstantCost(4));

        costs[(int)DefaultFunction.Blake2b_224] = new(
            LinearCost.InX(207616, 8310),
            new ConstantCost(4));

        costs[(int)DefaultFunction.Keccak_256] = new(
            LinearCost.InX(2261318, 64571),
            new ConstantCost(4));

        costs[(int)DefaultFunction.VerifyEd25519Signature] = new(
            LinearCost.InY(53384111, 14333),
            new ConstantCost(10));

        costs[(int)DefaultFunction.VerifyEcdsaSecp256k1Signature] = Const(43053543, 10);

        costs[(int)DefaultFunction.VerifySchnorrSecp256k1Signature] = new(
            LinearCost.InY(43574283, 26308),
            new ConstantCost(10));

        costs[(int)DefaultFunction.Ripemd_160] = new(
            LinearCost.InX(1964219, 24520),
            new ConstantCost(3));

        // ===== String operations =====

        costs[(int)DefaultFunction.AppendString] = new(
            new AddedSizesCost(1000, 59957),
            new AddedSizesCost(4, 1));

        costs[(int)DefaultFunction.EqualsString] = new(
            new LinearOnDiagonalCost(1000, 60594, 39184),
            new ConstantCost(1));

        costs[(int)DefaultFunction.EncodeUtf8] = new(
            LinearCost.InX(1000, 42921),
            LinearCost.InX(4, 2));

        costs[(int)DefaultFunction.DecodeUtf8] = new(
            LinearCost.InX(91189, 769),
            LinearCost.InX(4, 2));

        // ===== Control flow =====

        costs[(int)DefaultFunction.IfThenElse] = Const(76049, 1);
        costs[(int)DefaultFunction.ChooseUnit] = Const(61462, 4);
        costs[(int)DefaultFunction.Trace] = Const(59498, 32);

        // ===== Pair operations =====

        costs[(int)DefaultFunction.FstPair] = Const(141895, 32);
        costs[(int)DefaultFunction.SndPair] = Const(141992, 32);

        // ===== List operations =====

        costs[(int)DefaultFunction.ChooseList] = Const(132994, 32);
        costs[(int)DefaultFunction.MkCons] = Const(72362, 32);
        costs[(int)DefaultFunction.HeadList] = Const(83150, 32);
        costs[(int)DefaultFunction.TailList] = Const(81663, 32);
        costs[(int)DefaultFunction.NullList] = Const(74433, 32);

        // ===== Data operations =====

        costs[(int)DefaultFunction.ChooseData] = Const(94375, 32);
        costs[(int)DefaultFunction.ConstrData] = Const(22151, 32);
        costs[(int)DefaultFunction.MapData] = Const(68246, 32);
        costs[(int)DefaultFunction.ListData] = Const(33852, 32);
        costs[(int)DefaultFunction.IData] = Const(15299, 32);
        costs[(int)DefaultFunction.BData] = Const(11183, 32);
        costs[(int)DefaultFunction.UnConstrData] = Const(24588, 32);
        costs[(int)DefaultFunction.UnMapData] = Const(24623, 32);
        costs[(int)DefaultFunction.UnListData] = Const(25933, 32);
        costs[(int)DefaultFunction.UnIData] = Const(20744, 32);
        costs[(int)DefaultFunction.UnBData] = Const(20142, 32);

        costs[(int)DefaultFunction.EqualsData] = new(
            new MinSizeCost(898148, 27279),
            new ConstantCost(1));

        costs[(int)DefaultFunction.MkPairData] = Const(11546, 32);
        costs[(int)DefaultFunction.MkNilData] = Const(7243, 32);
        costs[(int)DefaultFunction.MkNilPairData] = Const(7391, 32);

        costs[(int)DefaultFunction.SerialiseData] = new(
            LinearCost.InX(955506, 213312),
            LinearCost.InX(0, 2));

        // ===== BLS12-381 G1 =====

        costs[(int)DefaultFunction.Bls12_381_G1_Add] = Const(962335, 18);
        costs[(int)DefaultFunction.Bls12_381_G1_Neg] = Const(267929, 18);

        costs[(int)DefaultFunction.Bls12_381_G1_ScalarMul] = new(
            LinearCost.InX(76433006, 8868),
            new ConstantCost(18));

        costs[(int)DefaultFunction.Bls12_381_G1_Equal] = Const(442008, 1);
        costs[(int)DefaultFunction.Bls12_381_G1_Compress] = Const(2780678, 6);
        costs[(int)DefaultFunction.Bls12_381_G1_Uncompress] = Const(52948122, 18);

        costs[(int)DefaultFunction.Bls12_381_G1_HashToGroup] = new(
            LinearCost.InX(52538055, 3756),
            new ConstantCost(18));

        costs[(int)DefaultFunction.Bls12_381_G1_MultiScalarMul] = new(
            LinearCost.InX(321837444, 25087669),
            new ConstantCost(18));

        // ===== BLS12-381 G2 =====

        costs[(int)DefaultFunction.Bls12_381_G2_Add] = Const(1995836, 36);
        costs[(int)DefaultFunction.Bls12_381_G2_Neg] = Const(284546, 36);

        costs[(int)DefaultFunction.Bls12_381_G2_ScalarMul] = new(
            LinearCost.InX(158221314, 26549),
            new ConstantCost(36));

        costs[(int)DefaultFunction.Bls12_381_G2_Equal] = Const(901022, 1);
        costs[(int)DefaultFunction.Bls12_381_G2_Compress] = Const(3227919, 12);
        costs[(int)DefaultFunction.Bls12_381_G2_Uncompress] = Const(74698472, 36);

        costs[(int)DefaultFunction.Bls12_381_G2_HashToGroup] = new(
            LinearCost.InX(166917843, 4307),
            new ConstantCost(36));

        costs[(int)DefaultFunction.Bls12_381_G2_MultiScalarMul] = new(
            LinearCost.InX(617887431, 67302824),
            new ConstantCost(36));

        // ===== BLS12-381 pairing =====

        costs[(int)DefaultFunction.Bls12_381_MillerLoop] = Const(254006273, 72);
        costs[(int)DefaultFunction.Bls12_381_MulMlResult] = Const(2174038, 72);
        costs[(int)DefaultFunction.Bls12_381_FinalVerify] = Const(333849714, 1);

        // ===== Byte/Integer conversion =====

        costs[(int)DefaultFunction.IntegerToByteString] = new(
            QuadraticCost.InZ(1293828, 28716, 63),
            new LiteralInYOrLinearInZCost(0, 1));

        costs[(int)DefaultFunction.ByteStringToInteger] = new(
            QuadraticCost.InY(1006041, 43623, 251),
            LinearCost.InY(0, 1));

        // ===== Bitwise operations =====

        costs[(int)DefaultFunction.AndByteString] = new(
            new LinearInYAndZCost(100181, 726, 719),
            new LinearInMaxYZCost(0, 1));

        costs[(int)DefaultFunction.OrByteString] = new(
            new LinearInYAndZCost(100181, 726, 719),
            new LinearInMaxYZCost(0, 1));

        costs[(int)DefaultFunction.XorByteString] = new(
            new LinearInYAndZCost(100181, 726, 719),
            new LinearInMaxYZCost(0, 1));

        costs[(int)DefaultFunction.ComplementByteString] = new(
            LinearCost.InX(107878, 680),
            LinearCost.InX(0, 1));

        costs[(int)DefaultFunction.ReadBit] = Const(95336, 1);

        costs[(int)DefaultFunction.WriteBits] = new(
            LinearCost.InY(281145, 18848),
            LinearCost.InX(0, 1));

        costs[(int)DefaultFunction.ReplicateByte] = new(
            LinearCost.InX(180194, 159),
            LinearCost.InX(1, 1));

        costs[(int)DefaultFunction.ShiftByteString] = new(
            LinearCost.InX(158519, 8942),
            LinearCost.InX(0, 1));

        costs[(int)DefaultFunction.RotateByteString] = new(
            LinearCost.InX(159378, 8813),
            LinearCost.InX(0, 1));

        costs[(int)DefaultFunction.CountSetBits] = new(
            LinearCost.InX(107490, 3298),
            new ConstantCost(1));

        costs[(int)DefaultFunction.FindFirstSetBit] = new(
            LinearCost.InX(106057, 655),
            new ConstantCost(1));

        // ===== Modular exponentiation =====

        costs[(int)DefaultFunction.ExpModInteger] = new(
            new ExpModCost(607153, 231697, 53144),
            LinearCost.InZ(0, 1));

        // ===== Array/List operations =====

        costs[(int)DefaultFunction.DropList] = new(
            LinearCost.InX(116711, 1957),
            new ConstantCost(4));

        costs[(int)DefaultFunction.LengthOfArray] = Const(231883, 10);

        costs[(int)DefaultFunction.ListToArray] = new(
            LinearCost.InX(1000, 24838),
            LinearCost.InX(7, 1));

        costs[(int)DefaultFunction.IndexArray] = Const(232010, 32);

        // ===== Value operations (V4) =====

        costs[(int)DefaultFunction.InsertCoin] = new(
            LinearCost.InX(356924, 18413),
            LinearCost.InX(45, 21));

        costs[(int)DefaultFunction.LookupCoin] = new(
            LinearCost.InZ(219951, 9444),
            new ConstantCost(1));

        costs[(int)DefaultFunction.UnionValue] = new(
            new WithInteractionCost(1000, 172116, 183150, 6),
            new AddedSizesCost(24, 21));

        costs[(int)DefaultFunction.ValueContains] = new(
            new ConstAboveDiagonalCost(213283, 0, 618401, 1998, 28258, 0, 0, 0),
            new ConstantCost(1));

        costs[(int)DefaultFunction.ValueData] = new(
            LinearCost.InX(1000, 38159),
            LinearCost.InX(2, 22));

        costs[(int)DefaultFunction.UnValueData] = new(
            QuadraticCost.InX(1000, 95933, 1),
            LinearCost.InX(1, 11));

        costs[(int)DefaultFunction.ScaleValue] = new(
            LinearCost.InY(1000, 277577),
            LinearCost.InY(12, 21));

        return costs;
    }
}
