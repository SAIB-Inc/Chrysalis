using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Header.Body;

[CborConverter(typeof(CustomListConverter))]
public record VrfCert(
    [CborProperty(0)] CborBytes Proof,
    [CborProperty(1)] CborBytes Output
) : CborBase;