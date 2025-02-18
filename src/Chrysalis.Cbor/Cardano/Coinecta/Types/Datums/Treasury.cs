using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Cardano.Types.Primitives;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Coinecta.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record Treasury(
    [CborIndex(0)]
    MultisigScript Owner,

    [CborIndex(1)]
    CborBytes TreasuryRootHash,

    [CborIndex(2)]
    PosixTime UnlockTime
) : CborBase;