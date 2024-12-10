using Chrysalis.Cardano.Core.Types.Primitives;
using Chrysalis.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Coinecta.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Treasury(
    [CborProperty(0)]
    MultisigScript Owner,

    [CborProperty(1)]
    CborBytes TreasuryRootHash,

    [CborProperty(2)]
    PosixTime UnlockTime
) : CborBase;