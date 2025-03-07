using System.Transactions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Plutus.Types;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Cbor.Handshake;

[CborConverter(typeof(UnionConverter))]
public abstract record LocalTxMonitorMessage : CborBase;

public class LocalTxMonitorMessages
{
    public static Acquire Acquire() => new(new ExactValue<CborInt>(new(1)));
    public static Release Release() => new(new ExactValue<CborInt>(new(3)));
    public static NextTx NextTx() => new(new ExactValue<CborInt>(new(5)));
    public static HasTx HasTx(string txId) => new(new ExactValue<CborInt>(new(7)), new(txId));
    public static GetSizes GetSizes() => new(new ExactValue<CborInt>(new(9)));
    public static GetMeasures GetMeasures() => new(new ExactValue<CborInt>(new(11)));


}

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Done(
    [CborIndex(0)][ExactValue(0)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Acquire(
    [CborIndex(0)][ExactValue(1)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Acquired(
    [CborIndex(0)][ExactValue(2)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborUlong Slot
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Release(
    [CborIndex(0)][ExactValue(3)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record NextTx(
    [CborIndex(0)][ExactValue(5)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(UnionConverter))]
public abstract record ReplyNextTx : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ReplyNextTxWithoutTx(
    [CborIndex(0)][ExactValue(6)] ExactValue<CborInt> Idx
) : ReplyNextTx;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ReplyNextTxWithTx(
    [CborIndex(0)][ExactValue(6)] ExactValue<CborInt> Idx,
    [CborIndex(1)] EraTx EraTx
) : ReplyNextTx;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record HasTx(
    [CborIndex(0)][ExactValue(7)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborText TxId
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ReplyHasTx(
    [CborIndex(0)][ExactValue(8)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborBool HasTx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record GetSizes(
    [CborIndex(0)][ExactValue(9)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ReplyGetSizes(
    [CborIndex(0)][ExactValue(10)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborList<CborUlong> Sizes
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record GetMeasures(
    [CborIndex(0)][ExactValue(11)] ExactValue<CborInt> Idx
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ReplyGetMeasures(
    [CborIndex(0)][ExactValue(12)] ExactValue<CborInt> Idx,
    [CborIndex(1)] CborUlong Slot,
    [CborIndex(2)] CborMap<CborText, CborDefList<CborInt>> Measures
) : LocalTxMonitorMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record Measures(
    [CborIndex(0)] CborInt CurrentSize,
    [CborIndex(1)] CborInt MaxCapacity
);



