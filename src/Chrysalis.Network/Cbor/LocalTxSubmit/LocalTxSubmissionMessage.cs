using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalTxSubmit;

[CborSerializable]
[CborUnion]
public abstract partial record LocalTxSubmissionMessage : CborBase;

public static class LocalTxSubmissionMessages
{
    public static SubmitTx SubmitTx(EraTx eraTx)
    {
        return new(0, eraTx);
    }

    public static AcceptTx AcceptTx()
    {
        return new(1);
    }

    public static RejectTx RejectTx(CborEncodedValue rejectReason)
    {
        return new(2, rejectReason);
    }

    public static Done Done()
    {
        return new(3);
    }
}

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record SubmitTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] EraTx EraTx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record AcceptTx(
    [CborOrder(0)] int Idx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record RejectTx(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue RejectReason
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record Done(
    [CborOrder(0)] int Idx
) : LocalTxSubmissionMessage;

[CborSerializable]
[CborList]
public partial record EraTx(
    [CborOrder(0)] int Era,
    [CborOrder(1)] CborEncodedValue Tx
) : CborBase;
