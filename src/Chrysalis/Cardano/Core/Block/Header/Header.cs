using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record BlockHeader(
    [CborProperty(0)] BlockHeaderBody HeaderBody,
    [CborProperty(1)] CborBytes BodySignature
) : RawCbor;
