using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.Common;

[CborSerializable]
public partial record Value0(int Value) : CborBase<Value0>;
public record Value0Validator : ICborValidator<Value0>
{
    public bool Validate(Value0 input) => input.Value == 0;
}

[CborSerializable]
public partial record Value1(int Value) : CborBase<Value1>;
public record Value1Validator : ICborValidator<Value1>
{
    public bool Validate(Value1 input) => input.Value == 1;
}

[CborSerializable]
public partial record Value2(int Value) : CborBase<Value2>;
public record Value2Validator : ICborValidator<Value2>
{
    public bool Validate(Value2 input) => input.Value == 2;
}

[CborSerializable]
public partial record Value3(int Value) : CborBase<Value3>;
public record Value3Validator : ICborValidator<Value3>
{
    public bool Validate(Value3 input) => input.Value == 3;
}

[CborSerializable]
public partial record Value4(int Value) : CborBase<Value4>;
public record Value4Validator : ICborValidator<Value4>
{
    public bool Validate(Value4 input) => input.Value == 4;
}

[CborSerializable]
public partial record Value5(int Value) : CborBase<Value5>;
public record Value5Validator : ICborValidator<Value5>
{
    public bool Validate(Value5 input) => input.Value == 5;
}

[CborSerializable]
public partial record Value6(int Value) : CborBase<Value6>;
public record Value6Validator : ICborValidator<Value6>
{
    public bool Validate(Value6 input) => input.Value == 6;
}

[CborSerializable]
public partial record Value7(int Value) : CborBase<Value7>;
public record Value7Validator : ICborValidator<Value7>
{
    public bool Validate(Value7 input) => input.Value == 7;
}

[CborSerializable]
public partial record Value8(int Value) : CborBase<Value8>;
public record Value8Validator : ICborValidator<Value8>
{
    public bool Validate(Value8 input) => input.Value == 8;
}

[CborSerializable]
public partial record Value9(int Value) : CborBase<Value9>;
public record Value9Validator : ICborValidator<Value9>
{
    public bool Validate(Value9 input) => input.Value == 9;
}