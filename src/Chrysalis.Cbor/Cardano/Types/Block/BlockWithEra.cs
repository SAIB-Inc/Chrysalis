using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block;

[CborConverter(typeof(CustomListConverter))]
public record BlockWithEra<T>(
    [CborIndex(0)] CborInt EraNumber,
    [CborIndex(1)] T Block
) : CborBase where T : Block;