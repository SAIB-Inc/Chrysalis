using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record ExUnits(
    [CborProperty(0)] CborUlong Mem,
    [CborProperty(1)] CborUlong Steps
) : RawCbor;
