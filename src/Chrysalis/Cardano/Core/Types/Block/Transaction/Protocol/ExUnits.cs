using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;

[CborConverter(typeof(CustomListConverter))]
public record ExUnits(
    [CborProperty(0)] CborUlong Mem,
    [CborProperty(1)] CborUlong Steps
) : CborBase;
