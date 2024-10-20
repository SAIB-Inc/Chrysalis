using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.List)]
public record ExUnits(
    [CborProperty(0)] CborUlong Mem,
    [CborProperty(1)] CborUlong Steps
) : RawCbor;
