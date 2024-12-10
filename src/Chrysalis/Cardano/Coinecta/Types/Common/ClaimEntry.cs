using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Coinecta.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record ClaimEntry(
    [CborProperty(0)]
    MultisigScript Claimant,

    [CborProperty(1)]
    MultiAssetOutput VestingValue,

    [CborProperty(2)]
    MultiAssetOutput DirectValue,

    [CborProperty(3)]
    CborBytes VestingParameters,

    [CborProperty(4)]
    CborBytes VestingProgram
) : CborBase;