using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

/// <summary>
/// An operational certificate used by stake pool operators to authorize block production.
/// </summary>
/// <param name="HotVKey">The hot verification key for block signing.</param>
/// <param name="SequenceNumber">The certificate sequence number, incremented with each rotation.</param>
/// <param name="KesPeriod">The KES period when this certificate becomes valid.</param>
/// <param name="Sigma">The cold key signature over the certificate data.</param>
[CborSerializable]
[CborList]
public partial record OperationalCert(
    [CborOrder(0)] byte[] HotVKey,
    [CborOrder(1)] ulong SequenceNumber,
    [CborOrder(2)] ulong KesPeriod,
    [CborOrder(3)] byte[] Sigma
) : CborBase;
