using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

// [CborSerializable]
[CborList]
public partial record OperationalCert(
[CborIndex(0)] byte[] HotVKey,
[CborIndex(1)] ulong SequenceNumber,
[CborIndex(2)] ulong KesPeriod,
[CborIndex(3)] byte[] Sigma
) : CborBase<OperationalCert>;
