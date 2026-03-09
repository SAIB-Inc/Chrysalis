using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ListBuiltins
{
    internal static CekValue HeadList(ImmutableArray<CekValue> args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        return list.Values.IsEmpty ? throw new EvaluationException("headList: empty list") : (CekValue)new VConstant(list.Values[0]);
    }

    internal static CekValue TailList(ImmutableArray<CekValue> args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        return list.Values.IsEmpty
            ? throw new EvaluationException("tailList: empty list")
            : (CekValue)new VConstant(new ListConstant(list.ItemType, list.Values.RemoveAt(0)));
    }

    internal static CekValue MkCons(ImmutableArray<CekValue> args)
    {
        Constant elem = UnwrapConstant(args[0]);
        ListConstant list = UnwrapListConstant(args[1]);
        return elem.ConstantType != list.ItemType
            ? throw new EvaluationException($"mkCons: type mismatch, expected {list.ItemType} got {elem.ConstantType}")
            : (CekValue)new VConstant(new ListConstant(list.ItemType, list.Values.Insert(0, elem)));
    }

    internal static CekValue NullList(ImmutableArray<CekValue> args) =>
        BoolResult(UnwrapListConstant(args[0]).Values.IsEmpty);

    internal static CekValue ChooseList(ImmutableArray<CekValue> args) =>
        UnwrapListConstant(args[0]).Values.IsEmpty ? args[1] : args[2];

    internal static CekValue DropList(ImmutableArray<CekValue> args)
    {
        BigInteger n = UnwrapInteger(args[0]);
        ListConstant list = UnwrapListConstant(args[1]);

        int drop = n < 0 ? 0 : n > list.Values.Length ? list.Values.Length : (int)n;
        ImmutableArray<Constant> remaining = drop == 0
            ? list.Values
            : [.. list.Values.Skip(drop)];

        return new VConstant(new ListConstant(list.ItemType, remaining));
    }
}
