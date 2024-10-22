using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Header;

[CborSerializable(CborType.List)]
public record OperationalCert(
    [CborProperty(0)] CborBytes HotVKey,
    [CborProperty(1)] CborUlong SequenceNumber,
    [CborProperty(2)] CborUlong KesPeriod,
    [CborProperty(3)] CborBytes Sigma
) : RawCbor;
