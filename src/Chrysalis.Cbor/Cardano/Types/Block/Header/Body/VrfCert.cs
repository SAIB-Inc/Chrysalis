using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public record VrfCert(
    [CborIndex(0)] CborBytes Proof,
    [CborIndex(1)] CborBytes Output
) : CborBase;