using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Address = Chrysalis.Cbor.Plutus.Types.Address.Address;

namespace Chrysalis.Cbor.Cardano.Crashr.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Payout(
  [CborIndex(0)] Address Address,
  [CborIndex(1)] MultiAssetOutput Amount
) : CborBase;