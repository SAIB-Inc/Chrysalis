using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block;

// [CborSerializable]
public partial record AuxiliaryDataSet(Dictionary<int, AuxiliaryData> Value) : CborBase<AuxiliaryDataSet>;