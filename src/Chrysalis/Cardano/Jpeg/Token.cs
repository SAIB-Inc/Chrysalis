using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Jpeg;

[CborSerializable(CborType.Constr, Index = 0)]
public record Token(
    [CborProperty(0)]
    CborInt TokenType,

    [CborProperty(1)]
    CborMap<CborBytes, CborUlong> Amount
) : RawCbor;