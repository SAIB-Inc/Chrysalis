using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ArrayBuiltins
{
    internal static CekValue ListToArray(ImmutableArray<CekValue> args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        return new VConstant(new ArrayConstant(list.ItemType, list.Values));
    }

    internal static CekValue LengthOfArray(ImmutableArray<CekValue> args)
    {
        return IntegerResult(UnwrapArrayConstant(args[0]).Values.Length);
    }

    internal static CekValue IndexArray(ImmutableArray<CekValue> args)
    {
        ArrayConstant arr = UnwrapArrayConstant(args[0]);
        BigInteger idx = UnwrapInteger(args[1]);
        return idx < 0 || idx >= arr.Values.Length
            ? throw new EvaluationException(
                $"indexArray: index {idx} out of bounds for array of length {arr.Values.Length}")
            : (CekValue)new VConstant(arr.Values[(int)idx]);
    }
}
