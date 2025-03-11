using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Coinecta.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record ClaimEntry(
    [CborIndex(0)]
    MultisigScript Claimant,

    [CborIndex(1)]
    MultiAssetOutput VestingValue,

    [CborIndex(2)]
    MultiAssetOutput DirectValue,

    [CborIndex(3)]
    CborBytes VestingParameters,

    [CborIndex(4)]
    CborBytes VestingProgram
) : CborBase;