using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public partial record OperationalCert(
    [CborIndex(0)] CborBytes HotVKey,
    [CborIndex(1)] CborUlong SequenceNumber,
    [CborIndex(2)] CborUlong KesPeriod,
    [CborIndex(3)] CborBytes Sigma
) : CborBase;
