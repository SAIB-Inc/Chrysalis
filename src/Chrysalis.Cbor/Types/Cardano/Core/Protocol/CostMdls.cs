using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
public partial record CostMdls(Dictionary<int, CborIndefList<long>> Value) : CborBase;