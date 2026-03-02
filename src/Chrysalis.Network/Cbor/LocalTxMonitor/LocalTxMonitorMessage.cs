using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.LocalTxSubmit;

namespace Chrysalis.Network.Cbor.LocalTxMonitor;

[CborSerializable]
[CborUnion]
public abstract partial record LocalTxMonitorMessage : CborBase;

public static class LocalTxMonitorMessages
{
    public static Acquire Acquire()
    {
        return new(1);
    }

    public static Release Release()
    {
        return new(3);
    }

    public static NextTx NextTx()
    {
        return new(5);
    }

    public static HasTx HasTx(string txId)
    {
        return new(7, txId);
    }

    public static GetSizes DefaultGetSizes => new(9);

    public static GetMeasures DefaultGetMeasures => new(11);
}

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record Done(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record Acquire(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Acquired(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slot
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Release(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record NextTx(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(6)]
public partial record ReplyNextTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)][CborNullable] EraTx? EraTx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record HasTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] string TxId
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(8)]
public partial record ReplyHasTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] bool HasTx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(9)]
public partial record GetSizes(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(10)]
public partial record ReplyGetSizes(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] List<ulong> Sizes
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(11)]
public partial record GetMeasures(
    [CborOrder(0)] int Idx
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
[CborIndex(12)]
public partial record ReplyGetMeasures(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slot,
    [CborOrder(2)] Dictionary<string, CborDefList<int>> Measures
) : LocalTxMonitorMessage;

[CborSerializable]
[CborList]
public partial record Measures(
    [CborOrder(0)] int CurrentSize,
    [CborOrder(1)] int MaxCapacity
) : CborBase;
