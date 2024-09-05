using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core
{
    [CborSerializable(CborType.List)]
    public record ExUnits(
        [CborProperty(0)] uint Mem,
        [CborProperty(1)] uint Steps
    ) : ICbor;
}