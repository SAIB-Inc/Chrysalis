using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.List)]
public record BlockHeader(
    [CborProperty(0)] BlockHeaderBody HeaderBody,
    [CborProperty(1)] CborBytes BodySignature
) : ICbor;

[CborSerializable(CborType.List)]
public record BlockHeaderBody(
    [CborProperty(0)] CborUlong BlockNum,
    [CborProperty(1)] CborUlong Slot,
    [CborProperty(2)] CborBytes? PrevHash,
    [CborProperty(3)] CborBytes IssuerVKey,
    [CborProperty(4)] CborBytes VrfVKey,
    [CborProperty(5)] CborBytes VrfResult,
    [CborProperty(6)] CborUlong BlockBodySize,
    [CborProperty(7)] CborBytes BlockBodyHash,
    [CborProperty(8)] CborBytes OperationalCert,
    [CborProperty(9)] CborBytes ProtocolVersion
) : ICbor;
