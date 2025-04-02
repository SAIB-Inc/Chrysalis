using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

[CborSerializable]
public partial record Value0(int Value) : CborBase;
public record Value0Validator : ICborValidator<Value0>
{
    public bool Validate(Value0 input) => input.Value == 0;
}

[CborSerializable]
public partial record Value1(int Value) : CborBase;
public record Value1Validator : ICborValidator<Value1>
{
    public bool Validate(Value1 input) => input.Value == 1;
}

[CborSerializable]
public partial record Value2(int Value) : CborBase;
public record Value2Validator : ICborValidator<Value2>
{
    public bool Validate(Value2 input) => input.Value == 2;
}

[CborSerializable]
public partial record Value3(int Value) : CborBase;
public record Value3Validator : ICborValidator<Value3>
{
    public bool Validate(Value3 input) => input.Value == 3;
}

[CborSerializable]
public partial record Value4(int Value) : CborBase;
public record Value4Validator : ICborValidator<Value4>
{
    public bool Validate(Value4 input) => input.Value == 4;
}

[CborSerializable]
public partial record Value5(int Value) : CborBase;
public record Value5Validator : ICborValidator<Value5>
{
    public bool Validate(Value5 input) => input.Value == 5;
}

[CborSerializable]
public partial record Value6(int Value) : CborBase;
public record Value6Validator : ICborValidator<Value6>
{
    public bool Validate(Value6 input) => input.Value == 6;
}

[CborSerializable]
public partial record Value7(int Value) : CborBase;
public record Value7Validator : ICborValidator<Value7>
{
    public bool Validate(Value7 input) => input.Value == 7;
}

[CborSerializable]
public partial record Value8(int Value) : CborBase;
public record Value8Validator : ICborValidator<Value8>
{
    public bool Validate(Value8 input) => input.Value == 8;
}

[CborSerializable]
public partial record Value9(int Value) : CborBase;
public record Value9Validator : ICborValidator<Value9>
{
    public bool Validate(Value9 input) => input.Value == 9;
}

[CborSerializable]
public partial record Value10(int Value) : CborBase;
public record Value10Validator : ICborValidator<Value10>
{
    public bool Validate(Value10 input) => input.Value == 10;
}

[CborSerializable]
public partial record Value11(int Value) : CborBase;
public record Value11Validator : ICborValidator<Value11>
{
    public bool Validate(Value11 input) => input.Value == 11;
}

[CborSerializable]
public partial record Value12(int Value) : CborBase;
public record Value12Validator : ICborValidator<Value12>
{
    public bool Validate(Value12 input) => input.Value == 12;
}