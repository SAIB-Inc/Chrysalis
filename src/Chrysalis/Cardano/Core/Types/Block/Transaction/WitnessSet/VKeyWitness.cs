using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core;

[CborConverter(typeof(CustomListConverter))]
public record VKeyWitness(
    [CborProperty(0)] CborBytes VKey,
    [CborProperty(1)] CborBytes Signature
) : CborBase;