using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block;

[CborConverter(typeof(MapConverter))]
public partial record AuxiliaryDataSet(Dictionary<CborInt, AuxiliaryData> Value) : CborBase;