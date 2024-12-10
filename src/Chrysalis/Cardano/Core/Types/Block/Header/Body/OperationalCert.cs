using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public record OperationalCert(
    [CborProperty(0)] CborBytes HotVKey,
    [CborProperty(1)] CborUlong SequenceNumber,
    [CborProperty(2)] CborUlong KesPeriod,
    [CborProperty(3)] CborBytes Sigma
) : CborBase;
