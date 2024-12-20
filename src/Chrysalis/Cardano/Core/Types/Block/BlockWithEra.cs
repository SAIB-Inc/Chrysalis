using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block;

[CborConverter(typeof(CustomListConverter))]
public record BlockWithEra<T>(
    [CborProperty(0)] CborInt EraNumber,
    [CborProperty(1)] T Block
) : CborBase where T : Block;