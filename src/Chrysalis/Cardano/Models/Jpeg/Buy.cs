using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record BuyRedeemer(
    [CborProperty(0)]
    CborInt Offset
) : RawCbor;
