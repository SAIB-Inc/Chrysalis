using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.List)]
public record Nonce(
    [CborProperty(0)] CborUlong Variant,
    [CborProperty(1)] CborBytes? Hash
) : ICbor;