using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ListBuiltins
{
    internal static CekValue HeadList(CekValue[] args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        if (list.IsListEmpty)
        {
            throw new EvaluationException("headList: empty list");
        }
        return new VConstant(list.ElementAt(0));
    }

    internal static CekValue TailList(CekValue[] args)
    {
        ListConstant list = UnwrapListConstant(args[0]);
        return list.IsListEmpty
            ? throw new EvaluationException("tailList: empty list")
            : new VConstant(new ListConstant(list.ItemType, list.Values) { Offset = list.Offset + 1 });
    }

    internal static CekValue MkCons(CekValue[] args)
    {
        Constant elem = UnwrapConstant(args[0]);
        ListConstant list = UnwrapListConstant(args[1]);
        if (elem.ConstantType != list.ItemType)
        {
            throw new EvaluationException($"mkCons: type mismatch, expected {list.ItemType} got {elem.ConstantType}");
        }

        ImmutableArray<Constant>.Builder builder = ImmutableArray.CreateBuilder<Constant>(list.Count + 1);
        builder.Add(elem);
        for (int i = 0; i < list.Count; i++)
        {
            builder.Add(list.ElementAt(i));
        }
        return new VConstant(new ListConstant(list.ItemType, builder.MoveToImmutable()));
    }

    internal static CekValue NullList(CekValue[] args) => BoolResult(UnwrapListConstant(args[0]).IsListEmpty);

    internal static CekValue ChooseList(CekValue[] args) => UnwrapListConstant(args[0]).IsListEmpty ? args[1] : args[2];

    internal static CekValue DropList(CekValue[] args)
    {
        BigInteger n = UnwrapInteger(args[0]);
        ListConstant list = UnwrapListConstant(args[1]);

        int drop = n < 0 ? 0 : n > list.Count ? list.Count : (int)n;
        return new VConstant(new ListConstant(list.ItemType, list.Values) { Offset = list.Offset + drop });
    }
}
