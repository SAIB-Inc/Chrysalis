
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Types.Test;

[CborSerializable]
[CborConstr(0)]
public partial record TestConstr(
    [CborOrder(0)]
    int IntValue,
    [CborOrder(1)][CborSize(32)]
    byte[] BoundedBytesValue,
    [CborOrder(2)]
    byte[] UnboundedBytesValue,
    [CborOrder(3)][CborNullable]
    CborEncodedValue? EncodedValue
) : CborBase<TestConstr>, ICborPreserveRaw;

public class TestConstrValidator : ICborValidator<TestConstr>
{
    public bool Validate(TestConstr input)
    {
        return true;
    }
}