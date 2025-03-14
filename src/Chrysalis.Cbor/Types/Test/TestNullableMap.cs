using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Test;

// Creating a nullable serializable type
// [CborSerializable]
[CborMap]
public partial record NullableTestMap(
    [property: CborProperty("0")]
    int Value1,
    [property: CborProperty("1")]
    string Value2
) : CborBase<NullableTestMap>;