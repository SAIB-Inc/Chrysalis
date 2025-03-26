using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Cbor.LocalTxMonitor;

[CborSerializable]
[CborUnion]
public abstract partial record LocalTxMonitorMessage : CborBase;

public class LocalTxMonitorMessages
{
    public static Acquire Acquire() => new(new(1));
    public static Release Release() => new(new(3));
    public static NextTx NextTx() => new(new(5));
    public static HasTx HasTx(string txId) => new(new(7), new(txId));
    public static GetSizes GetSizes() => new(new(9));
    public static GetMeasures GetMeasures() => new(new(11));


}

[CborSerializable]
[CborList]
public partial record Done(
    [CborOrder(0)] Value0 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record Acquire(
    [CborOrder(0)] Value1 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record Acquired(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] ulong Slot
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record Release(
    [CborOrder(0)] Value3 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record NextTx(
    [CborOrder(0)] Value5 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborUnion]
public abstract partial record ReplyNextTx : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record ReplyNextTxWithoutTx(
    [CborOrder(0)] Value6 Idx
) : ReplyNextTx;

[CborSerializable]
[CborList]
public partial record ReplyNextTxWithTx(
    [CborOrder(0)] Value6 Idx,
    [CborOrder(1)] EraTx EraTx
) : ReplyNextTx;

[CborSerializable]
[CborList]
public partial record HasTx(
    [CborOrder(0)] Value7 Idx,
    [CborOrder(1)] string TxId
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record ReplyHasTx(
    [CborOrder(0)] Value8 Idx,
    [CborOrder(1)] bool HasTx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record GetSizes(
    [CborOrder(0)] Value9 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record ReplyGetSizes(
    [CborOrder(0)] Value10 Idx,
    [CborOrder(1)] List<ulong> Sizes
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record GetMeasures(
    [CborOrder(0)] Value11 Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record ReplyGetMeasures(
    [CborOrder(0)] Value12 Idx,
    [CborOrder(1)] ulong Slot,
    [CborOrder(2)] Dictionary<string, CborDefList<int>> Measures
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record Measures(
    [CborOrder(0)] int CurrentSize,
    [CborOrder(1)] int MaxCapacity
) : CborBase;



