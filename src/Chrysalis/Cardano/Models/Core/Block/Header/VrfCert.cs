using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Header;

[CborSerializable(CborType.List)]
public record VrfCert(
    [CborProperty(0)] CborBytes Proof,
    [CborProperty(1)] CborBytes Output
) : RawCbor;