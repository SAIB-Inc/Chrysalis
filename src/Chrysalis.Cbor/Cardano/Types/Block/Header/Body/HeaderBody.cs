using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborConverter(typeof(UnionConverter))]
public abstract record BlockHeaderBody : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record AlonzoHeaderBody(
    [CborIndex(0)] CborUlong BlockNumber,
    [CborIndex(1)] CborUlong Slot,
    [CborIndex(2)] CborNullable<CborBytes> PrevHash,
    [CborIndex(3)] CborBytes IssuerVKey,
    [CborIndex(4)] CborBytes VrfVKey,
    [CborIndex(5)] VrfCert NonceVrf,
    [CborIndex(6)] VrfCert LeaderVrf,
    [CborIndex(7)] CborUlong BlockBodySize,
    [CborIndex(8)] CborBytes BlockBodyHash,
    [CborIndex(9)] CborBytes HotVKey,
    [CborIndex(10)] CborUlong OperationalCertSequenceNumber,
    [CborIndex(11)] CborUlong OperationalCertKesPeriod,
    [CborIndex(12)] CborBytes OperationalCertSigma,
    [CborIndex(13)] CborUlong ProtocolMajor,
    [CborIndex(14)] CborUlong ProtocolMinor
) : BlockHeaderBody;

[CborConverter(typeof(CustomListConverter))]
public record BabbageHeaderBody(
    [CborIndex(0)] CborUlong BlockNumber,
    [CborIndex(1)] CborUlong Slot,
    [CborIndex(2)] CborNullable<CborBytes> PrevHash,
    [CborIndex(3)] CborBytes IssuerVKey,
    [CborIndex(4)] CborBytes VrfVKey,
    [CborIndex(5)] VrfCert VrfResult,
    [CborIndex(6)] CborUlong BlockBodySize,
    [CborIndex(7)] CborBytes BlockBodyHash,
    [CborIndex(8)] OperationalCert OperationalCert,
    [CborIndex(9)] ProtocolVersion ProtocolVersion
) : BlockHeaderBody;