using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record OperationalCert(
    [CborProperty(0)] CborBytes HotVKey,
    [CborProperty(1)] CborUlong SequenceNumber,
    [CborProperty(2)] CborUlong KesPeriod,
    [CborProperty(3)] CborBytes Sigma
) : RawCbor;
