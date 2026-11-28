// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.Cbor;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class DataBuiltins
{
    // --- Constructors ---

    internal static CekValue ConstrData(CekValue[] args)
    {
        BigInteger tag = UnwrapInteger(args[0]);
        Constant[] fieldConstants = UnwrapList(args[1]);
        ImmutableArray<PlutusData>.Builder fields =
            ImmutableArray.CreateBuilder<PlutusData>(fieldConstants.Length);
        foreach (Constant c in fieldConstants)
        {
            if (c is not DataConstant d)
            {
                throw new EvaluationException(
                    $"constrData: expected list of data, got {c.GetType().Name}");
            }

            fields.Add(d.Value);
        }

        return DataResult(new PlutusDataConstr(tag, fields.MoveToImmutable()));
    }

    internal static CekValue MapData(CekValue[] args)
    {
        Constant[] pairConstants = UnwrapList(args[0]);
        ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder entries =
            ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>(pairConstants.Length);
        foreach (Constant c in pairConstants)
        {
            if (c is not PairConstant p
                || p.First is not DataConstant dk
                || p.Second is not DataConstant dv)
            {
                throw new EvaluationException(
                    $"mapData: expected list of pair<data,data>, got {c.GetType().Name}");
            }

            entries.Add((dk.Value, dv.Value));
        }

        return DataResult(new PlutusDataMap(entries.MoveToImmutable()));
    }

    internal static CekValue ListData(CekValue[] args)
    {
        Constant[] elConstants = UnwrapList(args[0]);
        ImmutableArray<PlutusData>.Builder values =
            ImmutableArray.CreateBuilder<PlutusData>(elConstants.Length);
        foreach (Constant c in elConstants)
        {
            if (c is not DataConstant d)
            {
                throw new EvaluationException(
                    $"listData: expected list of data, got {c.GetType().Name}");
            }

            values.Add(d.Value);
        }

        return DataResult(new PlutusDataList(values.MoveToImmutable()));
    }

    internal static CekValue IData(CekValue[] args) => DataResult(new PlutusDataInteger(UnwrapInteger(args[0])));

    internal static CekValue BData(CekValue[] args) => DataResult(new PlutusDataByteString(UnwrapByteString(args[0])));

    // --- Deconstructors ---

    internal static CekValue UnConstrData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        if (d is not PlutusDataConstr constr)
        {
            throw new EvaluationException(
                $"unConstrData: expected constr data, got {d.GetType().Name}");
        }

        return new VConstant(new PairConstant(
            ConstantType.PlutusInteger,
            new ListType(ConstantType.PlutusData),
            new IntegerConstant(constr.Tag),
            ConstrFields(constr)));
    }

    /// <summary>The fields of a constructor as the <c>list data</c> that <c>unConstrData</c> returns and a case on a Data constructor passes.</summary>
    internal static ListConstant ConstrFields(PlutusDataConstr constr)
    {
        ImmutableArray<Constant>.Builder fieldConstants =
            ImmutableArray.CreateBuilder<Constant>(constr.Fields.Length);
        foreach (PlutusData f in constr.Fields)
        {
            fieldConstants.Add(new DataConstant(f));
        }

        return new ListConstant(ConstantType.PlutusData, fieldConstants.MoveToImmutable());
    }

    internal static CekValue UnMapData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        if (d is not PlutusDataMap map)
        {
            throw new EvaluationException(
                $"unMapData: expected map data, got {d.GetType().Name}");
        }

        ImmutableArray<Constant>.Builder pairs =
            ImmutableArray.CreateBuilder<Constant>(map.Entries.Length);
        foreach ((PlutusData key, PlutusData value) in map.Entries)
        {
            pairs.Add(new PairConstant(
                ConstantType.PlutusData,
                ConstantType.PlutusData,
                new DataConstant(key),
                new DataConstant(value)));
        }

        return new VConstant(new ListConstant(
            new PairType(ConstantType.PlutusData, ConstantType.PlutusData),
            pairs.MoveToImmutable()));
    }

    internal static CekValue UnListData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        if (d is not PlutusDataList list)
        {
            throw new EvaluationException(
                $"unListData: expected list data, got {d.GetType().Name}");
        }

        ImmutableArray<Constant>.Builder constants =
            ImmutableArray.CreateBuilder<Constant>(list.Values.Length);
        foreach (PlutusData v in list.Values)
        {
            constants.Add(new DataConstant(v));
        }

        return new VConstant(new ListConstant(
            ConstantType.PlutusData,
            constants.MoveToImmutable()));
    }

    internal static CekValue UnIData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        return d is not PlutusDataInteger i
            ? throw new EvaluationException(
                $"unIData: expected integer data, got {d.GetType().Name}")
            : IntegerResult(i.Value);
    }

    internal static CekValue UnBData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        return d is not PlutusDataByteString bs
            ? throw new EvaluationException(
                $"unBData: expected bytestring data, got {d.GetType().Name}")
            : ByteStringResult(bs.Value);
    }

    // --- equalsData ---

    internal static CekValue EqualsData(CekValue[] args) => BoolResult(PlutusDataEquals(UnwrapData(args[0]), UnwrapData(args[1])));

    // --- chooseData ---

    internal static CekValue ChooseData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        return d switch
        {
            PlutusDataConstr => args[1],
            PlutusDataMap => args[2],
            PlutusDataList => args[3],
            PlutusDataInteger => args[4],
            PlutusDataByteString => args[5],
            _ => throw new EvaluationException(
                $"chooseData: unknown data type {d.GetType().Name}")
        };
    }

    // --- mkPairData ---

    internal static CekValue MkPairData(CekValue[] args)
    {
        PlutusData a = UnwrapData(args[0]);
        PlutusData b = UnwrapData(args[1]);
        return new VConstant(new PairConstant(
            ConstantType.PlutusData,
            ConstantType.PlutusData,
            new DataConstant(a),
            new DataConstant(b)));
    }

    // --- mkNilData / mkNilPairData ---

    internal static CekValue MkNilData(CekValue[] args)
    {
        UnwrapUnit(args[0]);
        return new VConstant(new ListConstant(ConstantType.PlutusData, []));
    }

    internal static CekValue MkNilPairData(CekValue[] args)
    {
        UnwrapUnit(args[0]);
        return new VConstant(new ListConstant(
            new PairType(ConstantType.PlutusData, ConstantType.PlutusData), []));
    }

    // --- serialiseData ---

    internal static CekValue SerialiseData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        byte[] encoded = CborWriter.EncodePlutusData(d);
        return ByteStringResult(encoded);
    }

    // --- Deep equality ---

    private record struct PairCursor(PlutusData A, PlutusData B, int Index, int Count);

    // Iterative and depth first: stops at the first difference without queueing siblings.
    private static bool PlutusDataEquals(PlutusData a, PlutusData b)
    {
        if (!SameNode(a, b, out int count))
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        PooledStack<PairCursor> open = new(16);
        try
        {
            open.Push(new(a, b, 0, count));
            while (open.Count > 0)
            {
                ref PairCursor top = ref open.Top;
                if (top.Index == top.Count)
                {
                    _ = open.Pop();
                    continue;
                }

                int index = top.Index++;
                PlutusData left = DataItems.At(top.A, index);
                PlutusData right = DataItems.At(top.B, index);
                if (!SameNode(left, right, out int childCount))
                {
                    return false;
                }

                if (childCount > 0)
                {
                    open.Push(new(left, right, 0, childCount));
                }
            }

            return true;
        }
        finally
        {
            open.Dispose();
        }
    }

    private static bool SameNode(PlutusData a, PlutusData b, out int count)
    {
        count = 0;
        if (a.GetType() != b.GetType())
        {
            return false;
        }

        switch (a)
        {
            case PlutusDataConstr ac:
                PlutusDataConstr bc = (PlutusDataConstr)b;
                count = DataItems.Count(ac);
                return ac.Tag == bc.Tag && count == DataItems.Count(bc);
            case PlutusDataMap or PlutusDataList:
                count = DataItems.Count(a);
                return count == DataItems.Count(b);
            case PlutusDataInteger ai:
                return ai.Value == ((PlutusDataInteger)b).Value;
            case PlutusDataByteString abs:
                return abs.Value.Span.SequenceEqual(((PlutusDataByteString)b).Value.Span);
            default:
                return false;
        }
    }
}
