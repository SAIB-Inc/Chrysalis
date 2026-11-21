using System.Numerics;
using Chrysalis.Codec.Test;
using Chrysalis.Plutus.Builtins;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using Xunit;
using static Chrysalis.Plutus.Types.DefaultFunction;

namespace Chrysalis.Plutus.Test;

public sealed class BoundsTests
{
    private static readonly BigInteger Pow2_63 = BigInteger.One << 63;
    private static readonly PairType PairData = new(ConstantType.PlutusData, ConstantType.PlutusData);

    public static TheoryData<string, bool, Term<DeBruijn>, bool> CheckOrderCases() => new()
    {
        { "sha2_256 65537 B (E)", true, App(Sha2_256, Bytes(65_537)), true },
        { "consByteString 256 (E)", true, App(ConsByteString, Int(256), Bytes(0)), true },
        { "consByteString 256 (C)", false, App(ConsByteString, Int(256), Bytes(0)), true },
        { "indexByteString 2^63", false, App(IndexByteString, Bytes(1), Int(Pow2_63)), true },
        { "readBit 2^63", false, App(ReadBit, Bytes(1), Int(Pow2_63)), true },
        { "indexArray 2^63 (E)", true, App(IndexArray, new ArrayConstant(ConstantType.PlutusInteger, [Int(0)]), Int(Pow2_63)), true },
        { "replicateByte 256", false, App(ReplicateByte, Int(1), Int(256)), true },
        { "sliceByteString 2^63", false, App(SliceByteString, Int(Pow2_63), Int(0), Bytes(0)), true },
        { "shiftByteString 2^63 (E)", true, App(ShiftByteString, Bytes(1), Int(Pow2_63)), true },
        { "constrData wrong list (C)", false, App(ConstrData, Int(0), List(ConstantType.PlutusInteger)), true },
        { "mapData wrong list (E)", true, App(MapData, List(ConstantType.PlutusData)), true },
        { "listData wrong list (C)", false, App(ListData, List(PairData)), true },
        { "writeBits wrong list (E)", true, App(WriteBits, Bytes(1), List(ConstantType.PlutusBool), BoolConstant.False), true },
        { "G1_multiScalarMul arg1 wrong list (C)", false, App(Bls12_381_G1_MultiScalarMul, List(ConstantType.PlutusInteger), List(ConstantType.Bls12381G2Element)), true },
        { "G2_multiScalarMul arg0 wrong list (E)", true, App(Bls12_381_G2_MultiScalarMul, List(ConstantType.PlutusBool), List(ConstantType.Bls12381G2Element)), true },
        { "listData non-empty wrong list (E)", true, App(ListData, List(ConstantType.PlutusInteger, Int(1))), true },
        { "G1_multiScalarMul arg0 wrong list (E)", true, App(Bls12_381_G1_MultiScalarMul, List(ConstantType.PlutusBool), List(ConstantType.Bls12381G1Element)), true },
        { "G2_multiScalarMul arg1 wrong list (C)", false, App(Bls12_381_G2_MultiScalarMul, List(ConstantType.PlutusInteger), List(ConstantType.Bls12381G1Element)), true },
        { "writeBits 4097 B (E)", true, App(WriteBits, Bytes(4097), List(ConstantType.PlutusInteger), BoolConstant.False), false },
        { "sha2_256 65537 B (C)", false, App(Sha2_256, Bytes(65_537)), false },
        { "indexByteString 5 (C)", false, App(IndexByteString, Bytes(1), Int(5)), false }
    };

    [Theory]
    [MemberData(nameof(CheckOrderCases))]
    public void BoundsAreCheckedWhereUpstreamChecksThem(string name, bool useLatestV3, Term<DeBruijn> term, bool throwsBeforeCharge)
    {
        _ = name;
        CostModel model = useLatestV3 ? CostModel.LatestV3 : CostModel.Default;
        ExBudget margin = new(1, 1);
        CekMachine machine = new(model.Machine.Startup + margin, model);
        if (throwsBeforeCharge)
        {
            _ = Assert.Throws<EvaluationException>(() => machine.TryRun(term, out _));
            Assert.Equal(BudgetStop.None, machine.StoppedAt);
            Assert.Equal(margin, machine.RemainingBudget);
        }
        else
        {
            Assert.False(machine.TryRun(term, out _));
            Assert.Equal(BudgetStop.Builtin, machine.StoppedAt);
        }
    }

    [Fact]
    public void BoundsCheckAllocatesNothing()
    {
        // Distinct PairType instance from BuiltinBounds' private static one to verify structural equality allocates nothing.
        PairType distinctPairData = new(ConstantType.PlutusData, ConstantType.PlutusData);
        (DefaultFunction Func, CekValue[] Args, Variant Variant)[] cases =
        [
            (AddInteger, [Val(Int(1)), Val(Int(2))], Variant.E),
            (Sha2_256, [Val(Bytes(64))], Variant.E),
            (SliceByteString, [Val(Int(0)), Val(Int(4)), Val(Bytes(64))], Variant.E),
            (ConstrData, [Val(Int(0)), Val(List(ConstantType.PlutusData))], Variant.E),
            (ConsByteString, [Val(Int(42)), Val(Bytes(4))], Variant.C),
            (WriteBits, [Val(Bytes(64)), Val(List(ConstantType.PlutusInteger)), Val(BoolConstant.True)], Variant.E),
            (Bls12_381_G1_ScalarMul, [Val(Int(7)), Val(new Bls12381G1Constant(new byte[48]))], Variant.E),
            (MapData, [Val(List(distinctPairData))], Variant.E),
            (Bls12_381_G1_MultiScalarMul, [Val(List(ConstantType.PlutusInteger)), Val(List(ConstantType.Bls12381G1Element))], Variant.E)
        ];

        int count = 0;
        long allocated = AllocationAssert.Measure(
            () => RunAll(cases),
            () =>
            {
                count = 0;
                for (int i = 0; i < 1000; i++)
                {
                    RunAll(cases);
                    count++;
                }
            },
            () => Assert.Equal(1000, count));
        Assert.Equal(0, allocated);

        static void RunAll((DefaultFunction Func, CekValue[] Args, Variant Variant)[] items)
        {
            foreach ((DefaultFunction func, CekValue[] args, Variant variant) in items)
            {
                BuiltinBounds.CheckArguments(func, args, variant);
                BuiltinBounds.CheckCharged(func, args, variant);
            }
        }
    }

    private static Term<DeBruijn> App(DefaultFunction func, params Constant[] args)
    {
        Term<DeBruijn> term = new BuiltinTerm<DeBruijn>(func);
        for (int i = 0; i < func.ForceCount(); i++)
        {
            term = new ForceTerm<DeBruijn>(term);
        }
        foreach (Constant arg in args)
        {
            term = new ApplyTerm<DeBruijn>(term, new ConstTerm<DeBruijn>(arg));
        }
        return term;
    }

    private static VConstant Val(Constant c) => new(c);
    private static IntegerConstant Int(BigInteger n) => new(n);
    private static ByteStringConstant Bytes(int length) => new(new byte[length]);
    private static ListConstant List(ConstantType itemType, params Constant[] items) => new(itemType, [.. items]);
}
