using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

/// <summary>
/// Abstract base for block header bodies across different Cardano eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record BlockHeaderBody : CborBase, ICborPreserveRaw { }

/// <summary>
/// Block header body for the Alonzo era with individual operational certificate fields.
/// </summary>
/// <param name="BlockNumber">The block number (height).</param>
/// <param name="Slot">The slot number in which the block was minted.</param>
/// <param name="PrevHash">The hash of the previous block, or null for the genesis block.</param>
/// <param name="IssuerVKey">The block issuer's verification key.</param>
/// <param name="VrfVKey">The VRF verification key.</param>
/// <param name="NonceVrf">The VRF certificate for the nonce.</param>
/// <param name="LeaderVrf">The VRF certificate for leader election.</param>
/// <param name="BlockBodySize">The size of the block body in bytes.</param>
/// <param name="BlockBodyHash">The hash of the block body.</param>
/// <param name="HotVKey">The operational certificate hot verification key.</param>
/// <param name="OperationalCertSequenceNumber">The operational certificate sequence number.</param>
/// <param name="OperationalCertKesPeriod">The KES period for the operational certificate.</param>
/// <param name="OperationalCertSigma">The operational certificate signature.</param>
/// <param name="ProtocolMajor">The major protocol version.</param>
/// <param name="ProtocolMinor">The minor protocol version.</param>
[CborSerializable]
[CborList]
public partial record AlonzoHeaderBody(
        [CborOrder(0)] ulong BlockNumber,
        [CborOrder(1)] ulong Slot,
        [CborOrder(2)][CborNullable] byte[] PrevHash,
        [CborOrder(3)] byte[] IssuerVKey,
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

/// <summary>
/// Block header body for the Babbage era with a structured operational certificate and protocol version.
/// </summary>
/// <param name="BlockNumber">The block number (height).</param>
/// <param name="Slot">The slot number in which the block was minted.</param>
/// <param name="PrevHash">The hash of the previous block, or null for the genesis block.</param>
/// <param name="IssuerVKey">The block issuer's verification key.</param>
/// <param name="VrfVKey">The VRF verification key.</param>
/// <param name="VrfResult">The VRF result certificate.</param>
/// <param name="BlockBodySize">The size of the block body in bytes.</param>
/// <param name="BlockBodyHash">The hash of the block body.</param>
/// <param name="OperationalCert">The operational certificate.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
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
