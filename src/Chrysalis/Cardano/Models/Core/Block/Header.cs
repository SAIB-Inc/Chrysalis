using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.List)]
public record BlockHeader(
    [CborProperty(0)] BlockHeaderBody HeaderBody,
    [CborProperty(1)] CborBytes BodySignature
) : RawCbor;
