using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public partial record OperationalCert(
    [CborOrder(0)] byte[] HotVKey,
    [CborOrder(1)] ulong SequenceNumber,
    [CborOrder(2)] ulong KesPeriod,
    [CborOrder(3)] byte[] Sigma
) : CborBase;
