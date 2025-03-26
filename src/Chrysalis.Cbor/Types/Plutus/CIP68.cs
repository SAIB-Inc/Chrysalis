using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Plutus;

[CborSerializable]
[CborConstr(0)]
public partial record Cip68<T>(
    [CborOrder(0)] PlutusData Metadata,
    [CborOrder(1)] int Version,
    [CborOrder(2)] T? Extra
) : CborBase;