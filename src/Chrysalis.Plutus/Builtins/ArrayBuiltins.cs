using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ArrayBuiltins
{
    internal static CekValue ListToArray(CekValue[] args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        if (list.Offset == 0)
        {
            return new VConstant(new ArrayConstant(list.ItemType, list.Values));
        }
        ImmutableArray<Constant>.Builder builder = ImmutableArray.CreateBuilder<Constant>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            builder.Add(list.ElementAt(i));
        }
        return new VConstant(new ArrayConstant(list.ItemType, builder.MoveToImmutable()));
    }

    internal static CekValue LengthOfArray(CekValue[] args) => IntegerResult(UnwrapArrayConstant(args[0]).Values.Length);

    internal static CekValue IndexArray(CekValue[] args)
    {
        ArrayConstant arr = UnwrapArrayConstant(args[0]);
        BigInteger idx = UnwrapInteger(args[1]);
        return idx < 0 || idx >= arr.Values.Length
            ? throw new EvaluationException(
                $"indexArray: index {idx} out of bounds for array of length {arr.Values.Length}")
            : new VConstant(arr.Values[(int)idx]);
    }
}
