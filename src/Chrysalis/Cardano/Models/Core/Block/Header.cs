using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(AlonzoBlockHeader),
])]
public record BlockHeader : ICbor;

[CborSerializable(CborType.List)]
public record AlonzoBlockHeader(
    [CborProperty(0)] BlockHeaderBody HeaderBody,
    [CborProperty(1)] CborBytes BodySignature
) : BlockHeader;
