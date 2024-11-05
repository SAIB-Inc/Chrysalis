using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record VrfCert(
    [CborProperty(0)] CborBytes Proof,
    [CborProperty(1)] CborBytes Output
) : RawCbor;