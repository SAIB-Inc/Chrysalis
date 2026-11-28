// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
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

    internal static CekValue IndexArray(CekValue[] args) => new VConstant(At(UnwrapArrayConstant(args[0]), UnwrapInteger(args[1]), "indexArray"));

    // Arrays.hs:48-80: the first count above the limit fails, as does an index outside the array. A message names no index, which may have 262,144 bits.
    private const int MaximumIndexCount = 1024;

    internal static CekValue MultiIndexArray(CekValue[] args)
    {
        ArrayConstant arr = UnwrapArrayConstant(args[0]);
        ListConstant indices = UnwrapListConstant(args[1]);
        if (indices.Count > MaximumIndexCount)
        {
            throw new EvaluationException($"multiIndexArray: too many indices (maximum is {MaximumIndexCount})");
        }
        ImmutableArray<Constant>.Builder items = ImmutableArray.CreateBuilder<Constant>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
        {
            items.Add(indices.ElementAt(i) is IntegerConstant index
                ? At(arr, index.Value, "multiIndexArray")
                : throw new EvaluationException("multiIndexArray: expected an integer index"));
        }
        return new VConstant(new ListConstant(arr.ItemType, items.MoveToImmutable()));
    }

    private static Constant At(ArrayConstant arr, BigInteger index, string name) =>
        index < 0 || index >= arr.Values.Length
            ? throw new EvaluationException($"{name}: index out of bounds for array of length {arr.Values.Length}")
            : arr.Values[(int)index];
}
