using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

[CborSerializable(CborType.List)]
public record Constitution(
    [CborProperty(0)] Anchor Anchor,
    [CborProperty(1)] CborNullable<CborBytes> ScriptHash
) : ICbor;