using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Address = Chrysalis.Plutus.Types.Address.Address;

namespace Chrysalis.Cardano.Crashr.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Payout(
  [CborProperty(0)] Address Address,
  [CborProperty(1)] MultiAssetOutput Amount
) : CborBase;