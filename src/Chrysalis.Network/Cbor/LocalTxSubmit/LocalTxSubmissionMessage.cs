using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalTxSubmit;

[CborSerializable]
[CborUnion]
public abstract partial record LocalTxSubmissionMessage : CborBase;

public class LocalTxSubmissionMessages
{
    public static SubmitTx SubmitTx(EraTx eraTx) => new(new(0), eraTx);
    public static AcceptTx AcceptTx() => new(new(1));
    public static RejectTx RejectTx(CborEncodedValue rejectReason) => new(new(2), rejectReason);
    public static Done Done() => new(new(3));
}

[CborSerializable]
[CborList]
public partial record SubmitTx(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] EraTx EraTx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
public partial record AcceptTx(
    [CborOrder(0)] Value1 Idx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
public partial record RejectTx(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] CborEncodedValue RejectReason
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
public partial record Done(
    [CborOrder(0)] Value3 Idx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
public partial record EraTx(
    [CborOrder(0)] int Era,
    [CborOrder(1)] CborEncodedValue Tx
) : CborBase;