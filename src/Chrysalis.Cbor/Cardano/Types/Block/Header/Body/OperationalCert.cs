using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborSerializable]
[CborList]
public partial record OperationalCert(
    [CborOrder(0)] byte[] HotVKey,
    [CborOrder(1)] ulong SequenceNumber,
    [CborOrder(2)] ulong KesPeriod,
    [CborOrder(3)] byte[] Sigma
) : CborBase<OperationalCert>;
