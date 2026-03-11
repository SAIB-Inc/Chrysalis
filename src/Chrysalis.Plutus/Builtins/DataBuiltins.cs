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

    internal static CekValue IData(CekValue[] args)
    {
        return DataResult(new PlutusDataInteger(UnwrapInteger(args[0])));
    }

    internal static CekValue BData(CekValue[] args)
    {
        return DataResult(new PlutusDataByteString(UnwrapByteString(args[0])));
    }

    // --- Deconstructors ---

    internal static CekValue UnConstrData(CekValue[] args)
    {
        PlutusData d = UnwrapData(args[0]);
        if (d is not PlutusDataConstr constr)
        {
            throw new EvaluationException(
                $"unConstrData: expected constr data, got {d.GetType().Name}");
        }

        ImmutableArray<Constant>.Builder fieldConstants =
            ImmutableArray.CreateBuilder<Constant>(constr.Fields.Length);
        foreach (PlutusData f in constr.Fields)
        {
            fieldConstants.Add(new DataConstant(f));
        }

        return new VConstant(new PairConstant(
            ConstantType.PlutusInteger,
            new ListType(ConstantType.PlutusData),
            new IntegerConstant(constr.Tag),
            new ListConstant(ConstantType.PlutusData, fieldConstants.MoveToImmutable())));
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

    internal static CekValue EqualsData(CekValue[] args)
    {
        return BoolResult(PlutusDataEquals(UnwrapData(args[0]), UnwrapData(args[1])));
    }

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

    private static bool PlutusDataEquals(PlutusData a, PlutusData b)
    {
        if (a.GetType() != b.GetType())
        {
            return false;
        }

        switch (a)
        {
            case PlutusDataConstr ac:
                {
                    PlutusDataConstr bc = (PlutusDataConstr)b;
                    if (ac.Tag != bc.Tag || ac.Fields.Length != bc.Fields.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < ac.Fields.Length; i++)
                    {
                        if (!PlutusDataEquals(ac.Fields[i], bc.Fields[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            case PlutusDataMap am:
                {
                    PlutusDataMap bm = (PlutusDataMap)b;
                    if (am.Entries.Length != bm.Entries.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < am.Entries.Length; i++)
                    {
                        if (!PlutusDataEquals(am.Entries[i].Key, bm.Entries[i].Key) ||
                            !PlutusDataEquals(am.Entries[i].Value, bm.Entries[i].Value))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            case PlutusDataList al:
                {
                    PlutusDataList bl = (PlutusDataList)b;
                    if (al.Values.Length != bl.Values.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < al.Values.Length; i++)
                    {
                        if (!PlutusDataEquals(al.Values[i], bl.Values[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            case PlutusDataInteger ai:
                return ai.Value == ((PlutusDataInteger)b).Value;
            case PlutusDataByteString abs:
                return abs.Value.Span.SequenceEqual(((PlutusDataByteString)b).Value.Span);
            default:
                return false;
        }
    }
}
