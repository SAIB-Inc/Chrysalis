using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block;

[CborConverter(typeof(MapConverter))]
public record AuxiliaryDataSet(Dictionary<CborInt, AuxiliaryData> Value) : CborBase;