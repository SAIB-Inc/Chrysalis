using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Size measurement for builtin costing.
/// Converts runtime values to cost model input sizes.
/// </summary>
internal static class ExMem
{
    // --- Primitive size functions ---

    internal static int IntegerExMem(BigInteger n)
    {
        if (n.IsZero)
        {
            return 1;
        }

        long bits = BigInteger.Abs(n).GetBitLength();
        return (int)((bits - 1) / 64) + 1;
    }

    internal static int ByteStringExMem(ReadOnlyMemory<byte> bs) =>
        bs.Length == 0 ? 1 : ((bs.Length - 1) / 8) + 1;

    internal static int StringExMem(string s) =>
        System.Text.Encoding.UTF8.GetByteCount(s);

    internal static int DataExMem(PlutusData d)
    {
        int total = 0;
        Stack<PlutusData> stack = new();
        stack.Push(d);
        while (stack.Count > 0)
        {
            PlutusData current = stack.Pop();
            total += 4;
            switch (current)
            {
                case PlutusDataConstr constr:
                    for (int i = constr.Fields.Length - 1; i >= 0; i--)
                    {
                        stack.Push(constr.Fields[i]);
                    }

                    break;
                case PlutusDataMap map:
                    for (int i = map.Entries.Length - 1; i >= 0; i--)
                    {
                        stack.Push(map.Entries[i].Value);
                        stack.Push(map.Entries[i].Key);
                    }
                    break;
                case PlutusDataList list:
                    for (int i = list.Values.Length - 1; i >= 0; i--)
                    {
                        stack.Push(list.Values[i]);
                    }

                    break;
                case PlutusDataInteger integer:
                    total += IntegerExMem(integer.Value);
                    break;
                case PlutusDataByteString bs:
                    total += ByteStringExMem(bs.Value);
                    break;
                default:
                    break;
            }
        }
        return total;
    }

    internal static int DataNodeCount(PlutusData d)
    {
        int total = 0;
        Stack<PlutusData> stack = new();
        stack.Push(d);
        while (stack.Count > 0)
        {
            PlutusData current = stack.Pop();
            total += 1;
            switch (current)
            {
                case PlutusDataConstr constr:
                    for (int i = constr.Fields.Length - 1; i >= 0; i--)
                    {
                        stack.Push(constr.Fields[i]);
                    }

                    break;
                case PlutusDataMap map:
                    for (int i = map.Entries.Length - 1; i >= 0; i--)
                    {
                        stack.Push(map.Entries[i].Value);
                        stack.Push(map.Entries[i].Key);
                    }
                    break;
                case PlutusDataList list:
                    for (int i = list.Values.Length - 1; i >= 0; i--)
                    {
                        stack.Push(list.Values[i]);
                    }

                    break;
                default:
                    break;
            }
        }
        return total;
    }

    internal static int SizeExMemFromInt(int value) =>
        value <= 0 ? 0 : ((value - 1) / 8) + 1;

    internal static long IntegerCostedLiterally(BigInteger n)
    {
        BigInteger abs = BigInteger.Abs(n);
        return abs > long.MaxValue ? long.MaxValue : (long)abs;
    }

    internal static int ValueSizeExMem(LedgerValue v)
    {
        int size = 0;
        foreach (CurrencyEntry entry in v.Entries)
        {
            size += entry.Tokens.Length;
        }
        return size;
    }

    internal static int ValueMaxDepth(LedgerValue v)
    {
        int outerSize = v.Entries.Length;
        int maxInner = 0;
        foreach (CurrencyEntry entry in v.Entries)
        {
            if (entry.Tokens.Length > maxInner)
            {
                maxInner = entry.Tokens.Length;
            }
        }
        int logOuter = outerSize > 0 ? (int)Math.Floor(Math.Log2(outerSize)) + 1 : 0;
        int logInner = maxInner > 0 ? (int)Math.Floor(Math.Log2(maxInner)) + 1 : 0;
        return logOuter + logInner;
    }

    // BLS constants
    internal const int G1ExMem = 18;
    internal const int G2ExMem = 36;

    // --- Size extraction helpers (from CekValue) ---

    private static long IntSize(CekValue val) =>
        val is VConstant { Value: IntegerConstant i } ? IntegerExMem(i.Value) : 1;

    private static long BsSize(CekValue val) =>
        val is VConstant { Value: ByteStringConstant bs } ? ByteStringExMem(bs.Value) : 0;

    private static long StrSize(CekValue val) =>
        val is VConstant { Value: StringConstant s } ? StringExMem(s.Value) : 0;

    private static long DataSize(CekValue val) =>
        val is VConstant { Value: DataConstant d } ? DataExMem(d.Value) : 4;

    private static long ListSizeFromArg(CekValue val) =>
        val is VConstant { Value: ListConstant l } ? l.Values.Length : 0;

    private static long SizeExMemFromArg(CekValue val) =>
        val is VConstant { Value: IntegerConstant i }
        && i.Value is var n
        && n <= long.MaxValue
        && n >= -long.MaxValue
            ? SizeExMemFromInt((int)n)
            : 0;

    private static long IntLiteralValue(CekValue val) =>
        val is VConstant { Value: IntegerConstant i } ? IntegerCostedLiterally(i.Value) : 0;

    private static long ValueSizeFromArg(CekValue val) =>
        val is VConstant { Value: ValueConstant v } ? ValueSizeExMem(v.Value) : 0;

    private static long ValueMaxDepthFromArg(CekValue val) =>
        val is VConstant { Value: ValueConstant v } ? ValueMaxDepth(v.Value) : 0;

    private static long DataNodeCountFromArg(CekValue val) =>
        val is VConstant { Value: DataConstant d } ? DataNodeCount(d.Value) : 0;

    // --- Main dispatch: compute argument sizes for a builtin ---

    internal static (long X, long Y, long Z) ComputeArgSizes(
        DefaultFunction func, ImmutableArray<CekValue> args) =>
        func switch
        {
            // 2-arg integer operations: (int, int)
            DefaultFunction.AddInteger or
            DefaultFunction.SubtractInteger or
            DefaultFunction.MultiplyInteger or
            DefaultFunction.DivideInteger or
            DefaultFunction.QuotientInteger or
            DefaultFunction.RemainderInteger or
            DefaultFunction.ModInteger or
            DefaultFunction.EqualsInteger or
            DefaultFunction.LessThanInteger or
            DefaultFunction.LessThanEqualsInteger =>
                (IntSize(args[0]), IntSize(args[1]), 0),

            // 2-arg bytestring operations: (bs, bs)
            DefaultFunction.AppendByteString or
            DefaultFunction.EqualsByteString or
            DefaultFunction.LessThanByteString or
            DefaultFunction.LessThanEqualsByteString =>
                (BsSize(args[0]), BsSize(args[1]), 0),

            // (int, bs)
            DefaultFunction.ConsByteString =>
                (IntSize(args[0]), BsSize(args[1]), 0),

            // (int, int, bs) - slice
            DefaultFunction.SliceByteString =>
                (IntSize(args[0]), IntSize(args[1]), BsSize(args[2])),

            // 1-arg bytestring
            DefaultFunction.LengthOfByteString or
            DefaultFunction.Sha2_256 or
            DefaultFunction.Sha3_256 or
            DefaultFunction.Blake2b_256 or
            DefaultFunction.Blake2b_224 or
            DefaultFunction.Keccak_256 or
            DefaultFunction.Ripemd_160 or
            DefaultFunction.ComplementByteString or
            DefaultFunction.CountSetBits or
            DefaultFunction.FindFirstSetBit =>
                (BsSize(args[0]), 0, 0),

            // (bs, int)
            DefaultFunction.IndexByteString or
            DefaultFunction.ReadBit =>
                (BsSize(args[0]), IntSize(args[1]), 0),

            // (bs, IntegerCostedLiterally)
            DefaultFunction.ShiftByteString or
            DefaultFunction.RotateByteString =>
                (BsSize(args[0]), IntLiteralValue(args[1]), 0),

            // dropList: (IntegerCostedLiterally, list)
            DefaultFunction.DropList =>
                (IntLiteralValue(args[0]), ListSizeFromArg(args[1]), 0),

            // replicateByte: (sizeExMem, int)
            DefaultFunction.ReplicateByte =>
                (SizeExMemFromArg(args[0]), IntSize(args[1]), 0),

            // 3-arg signature verification: (bs, bs, bs)
            DefaultFunction.VerifyEd25519Signature or
            DefaultFunction.VerifyEcdsaSecp256k1Signature or
            DefaultFunction.VerifySchnorrSecp256k1Signature =>
                (BsSize(args[0]), BsSize(args[1]), BsSize(args[2])),

            // 2-arg string operations: (str, str)
            DefaultFunction.AppendString or
            DefaultFunction.EqualsString =>
                (StrSize(args[0]), StrSize(args[1]), 0),

            // 1-arg string/bs
            DefaultFunction.EncodeUtf8 => (StrSize(args[0]), 0, 0),
            DefaultFunction.DecodeUtf8 => (BsSize(args[0]), 0, 0),

            // Constant cost builtins (sizes don't matter)
            DefaultFunction.IfThenElse or
            DefaultFunction.ChooseUnit or
            DefaultFunction.Trace or
            DefaultFunction.FstPair or
            DefaultFunction.SndPair or
            DefaultFunction.ChooseList or
            DefaultFunction.MkCons or
            DefaultFunction.HeadList or
            DefaultFunction.TailList or
            DefaultFunction.NullList or
            DefaultFunction.ChooseData or
            DefaultFunction.ConstrData or
            DefaultFunction.MapData or
            DefaultFunction.ListData or
            DefaultFunction.IData or
            DefaultFunction.BData or
            DefaultFunction.UnConstrData or
            DefaultFunction.UnMapData or
            DefaultFunction.UnListData or
            DefaultFunction.UnIData or
            DefaultFunction.UnBData or
            DefaultFunction.MkPairData or
            DefaultFunction.MkNilData or
            DefaultFunction.MkNilPairData or
            DefaultFunction.Bls12_381_G1_Add or
            DefaultFunction.Bls12_381_G1_Neg or
            DefaultFunction.Bls12_381_G1_Equal or
            DefaultFunction.Bls12_381_G1_Compress or
            DefaultFunction.Bls12_381_G1_Uncompress or
            DefaultFunction.Bls12_381_G2_Add or
            DefaultFunction.Bls12_381_G2_Neg or
            DefaultFunction.Bls12_381_G2_Equal or
            DefaultFunction.Bls12_381_G2_Compress or
            DefaultFunction.Bls12_381_G2_Uncompress or
            DefaultFunction.Bls12_381_MillerLoop or
            DefaultFunction.Bls12_381_MulMlResult or
            DefaultFunction.Bls12_381_FinalVerify or
            DefaultFunction.LengthOfArray or
            DefaultFunction.IndexArray =>
                (0, 0, 0),

            // (data, data) - equalsData
            DefaultFunction.EqualsData =>
                (DataSize(args[0]), DataSize(args[1]), 0),

            // 1-arg data
            DefaultFunction.SerialiseData =>
                (DataSize(args[0]), 0, 0),

            // BLS scalar mul: (int, g1/g2)
            DefaultFunction.Bls12_381_G1_ScalarMul =>
                (IntSize(args[0]), G1ExMem, 0),
            DefaultFunction.Bls12_381_G2_ScalarMul =>
                (IntSize(args[0]), G2ExMem, 0),

            // BLS hash to group: (bs, bs)
            DefaultFunction.Bls12_381_G1_HashToGroup or
            DefaultFunction.Bls12_381_G2_HashToGroup =>
                (BsSize(args[0]), BsSize(args[1]), 0),

            // BLS multi scalar mul: (list, list)
            DefaultFunction.Bls12_381_G1_MultiScalarMul or
            DefaultFunction.Bls12_381_G2_MultiScalarMul =>
                (ListSizeFromArg(args[0]), ListSizeFromArg(args[1]), 0),

            // integerToByteString: (bool, sizeExMem, int)
            DefaultFunction.IntegerToByteString =>
                (1, SizeExMemFromArg(args[1]), IntSize(args[2])),

            // byteStringToInteger: (bool, bs)
            DefaultFunction.ByteStringToInteger =>
                (1, BsSize(args[1]), 0),

            // 3-arg bitwise: (bool, bs, bs)
            DefaultFunction.AndByteString or
            DefaultFunction.OrByteString or
            DefaultFunction.XorByteString =>
                (1, BsSize(args[1]), BsSize(args[2])),

            // writeBits: (bs, list, list)
            DefaultFunction.WriteBits =>
                (BsSize(args[0]), ListSizeFromArg(args[1]), ListSizeFromArg(args[2])),

            // expModInteger: (int, int, int)
            DefaultFunction.ExpModInteger =>
                (IntSize(args[0]), IntSize(args[1]), IntSize(args[2])),

            // listToArray: (list)
            DefaultFunction.ListToArray =>
                (ListSizeFromArg(args[0]), 0, 0),

            // Value operations
            DefaultFunction.InsertCoin =>
                (ValueMaxDepthFromArg(args[3]), 0, 0),
            DefaultFunction.LookupCoin =>
                (BsSize(args[0]), BsSize(args[1]), ValueMaxDepthFromArg(args[2])),
            DefaultFunction.UnionValue =>
                (ValueSizeFromArg(args[0]), ValueSizeFromArg(args[1]), 0),
            DefaultFunction.ValueContains =>
                (ValueSizeFromArg(args[0]), ValueSizeFromArg(args[1]), 0),
            DefaultFunction.ValueData =>
                (ValueSizeFromArg(args[0]), 0, 0),
            DefaultFunction.UnValueData =>
                (DataNodeCountFromArg(args[0]), 0, 0),
            DefaultFunction.ScaleValue =>
                (IntSize(args[0]), ValueSizeFromArg(args[1]), 0),

            _ => (0, 0, 0)
        };
}
