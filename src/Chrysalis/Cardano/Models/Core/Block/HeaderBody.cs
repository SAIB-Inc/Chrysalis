using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(BabbageHeaderBody),
    typeof(AlonzoHeaderBody),
])]
public record BlockHeaderBody : RawCbor;

[CborSerializable(CborType.List)]
public record BabbageHeaderBody(
    [CborProperty(0)] CborUlong BlockNumber,
    [CborProperty(1)] CborUlong Slot,
    [CborProperty(2)] CborNullable<CborBytes> PrevHash,
    [CborProperty(3)] CborBytes IssuerVKey,
    [CborProperty(4)] CborBytes VrfVKey,
    [CborProperty(5)] VrfCert VrfResult,
    [CborProperty(6)] CborUlong BlockBodySize,
    [CborProperty(7)] CborBytes BlockBodyHash,
    [CborProperty(8)] OperationalCert OperationalCert,
    [CborProperty(9)] ProtocolVersion ProtocolVersion
) : BlockHeaderBody;

[CborSerializable(CborType.List)]
public record AlonzoHeaderBody(
    [CborProperty(0)] CborUlong BlockNumber,
    [CborProperty(1)] CborUlong Slot,
    [CborProperty(2)] CborNullable<CborBytes> PrevHash,
    [CborProperty(3)] CborBytes IssuerVKey,
    [CborProperty(4)] CborBytes VrfVKey,
    [CborProperty(5)] VrfCert NonceVrf,
    [CborProperty(6)] VrfCert LeaderVrf,
    [CborProperty(7)] CborUlong BlockBodySize,
    [CborProperty(8)] CborBytes BlockBodyHash,
    [CborProperty(9)] CborBytes HotVKey,
    [CborProperty(10)] CborUlong SequenceNumber,
    [CborProperty(11)] CborUlong KesPeriod,
    [CborProperty(12)] CborBytes Sigma,
    [CborProperty(13)] CborUlong ProtocolMajor,
    [CborProperty(14)] CborUlong ProtocolMinor
) : BlockHeaderBody;