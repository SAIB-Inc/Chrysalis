using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalTxSubmit;

[CborConverter(typeof(UnionConverter))]
public abstract record LocalTxSubmissionMessage : CborBase;

public class LocalTxSubmissionMessages
{
    public static SubmitTx SubmitTx(EraTx eraTx) =>
        new(new ExactValue<CborInt>(new(0)), eraTx);

    public static AcceptTx AcceptTx() => new(new ExactValue<CborInt>(new(1)));

    public static RejectTx RejectTx(CborEncodedValue rejectReason) =>
        new(new ExactValue<CborInt>(new(2)), rejectReason);

    public static Done Done() => new(new ExactValue<CborInt>(new(3)));
}

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record SubmitTx(
    [CborIndex(0)] [ExactValue(0)]
    ExactValue<CborInt> Idx,

    [CborIndex(1)]
    EraTx EraTx
) : LocalTxSubmissionMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record AcceptTx(
    [CborIndex(0)] [ExactValue(1)]
    ExactValue<CborInt> Idx
) : LocalTxSubmissionMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record RejectTx(
    [CborIndex(0)] [ExactValue(2)]
    ExactValue<CborInt> Idx,

    [CborIndex(1)]
    CborEncodedValue RejectReason
) : LocalTxSubmissionMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Done(
    [CborIndex(0)] [ExactValue(3)]
    ExactValue<CborInt> Idx
) : LocalTxSubmissionMessage;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record EraTx(
    [CborIndex(0)]
    CborInt Era,

    [CborIndex(1)]
    CborEncodedValue Tx
) : CborBase;