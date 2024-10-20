using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Token(
    [CborProperty(0)]
    CborInt TokenType,

    [CborProperty(1)]
    CborMap<CborBytes, CborUlong> Amount
) : RawCbor;