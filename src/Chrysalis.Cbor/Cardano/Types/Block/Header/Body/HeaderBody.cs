using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborSerializable]
[CborUnion]
public abstract partial record BlockHeaderBody : CborBase<BlockHeaderBody>
{
    [CborSerializable]
    [CborList]
    public partial record AlonzoHeaderBody(
            [CborOrder(0)] ulong BlockNumber,
            [CborOrder(1)] ulong Slot,
            [CborOrder(2)][CborNullable] byte[] PrevHash,
            [CborOrder(3)] ulong IssuerVKey,
            [CborOrder(4)] byte[] VrfVKey,
            [CborOrder(5)] VrfCert NonceVrf,
            [CborOrder(6)] VrfCert LeaderVrf,
            [CborOrder(7)] ulong BlockBodySize,
            [CborOrder(8)] byte[] BlockBodyHash,
            [CborOrder(9)] byte[] HotVKey,
            [CborOrder(10)] ulong OperationalCertSequenceNumber,
            [CborOrder(11)] ulong OperationalCertKesPeriod,
            [CborOrder(12)] byte[] OperationalCertSigma,
            [CborOrder(13)] ulong ProtocolMajor,
            [CborOrder(14)] ulong ProtocolMinor
        ) : BlockHeaderBody;

    [CborSerializable]
    [CborList]
    public partial record BabbageHeaderBody(
        [CborOrder(0)] ulong BlockNumber,
        [CborOrder(1)] ulong Slot,
        [CborOrder(2)][CborNullable] byte[] PrevHash,
        [CborOrder(3)] byte[] IssuerVKey,
        [CborOrder(4)] byte[] VrfVKey,
        [CborOrder(5)] VrfCert VrfResult,
        [CborOrder(6)] ulong BlockBodySize,
        [CborOrder(7)] byte[] BlockBodyHash,
        [CborOrder(8)] OperationalCert OperationalCert,
        [CborOrder(9)] ProtocolVersion ProtocolVersion
    ) : BlockHeaderBody;
}
