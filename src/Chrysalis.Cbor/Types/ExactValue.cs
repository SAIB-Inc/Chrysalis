using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// A CBOR integer that must have the exact value 0.
/// </summary>
/// <param name="Value">The integer value, validated to equal 0.</param>
[CborSerializable]
public partial record Value0(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value0"/> instance contains the value 0.
/// </summary>
public record Value0Validator : ICborValidator<Value0>
{
    /// <summary>
    /// Validates that the input value equals 0.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 0; otherwise false.</returns>
    public bool Validate(Value0 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 0;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 1.
/// </summary>
/// <param name="Value">The integer value, validated to equal 1.</param>
[CborSerializable]
public partial record Value1(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value1"/> instance contains the value 1.
/// </summary>
public record Value1Validator : ICborValidator<Value1>
{
    /// <summary>
    /// Validates that the input value equals 1.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 1; otherwise false.</returns>
    public bool Validate(Value1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 1;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 2.
/// </summary>
/// <param name="Value">The integer value, validated to equal 2.</param>
[CborSerializable]
public partial record Value2(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value2"/> instance contains the value 2.
/// </summary>
public record Value2Validator : ICborValidator<Value2>
{
    /// <summary>
    /// Validates that the input value equals 2.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 2; otherwise false.</returns>
    public bool Validate(Value2 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 2;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 3.
/// </summary>
/// <param name="Value">The integer value, validated to equal 3.</param>
[CborSerializable]
public partial record Value3(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value3"/> instance contains the value 3.
/// </summary>
public record Value3Validator : ICborValidator<Value3>
{
    /// <summary>
    /// Validates that the input value equals 3.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 3; otherwise false.</returns>
    public bool Validate(Value3 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 3;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 4.
/// </summary>
/// <param name="Value">The integer value, validated to equal 4.</param>
[CborSerializable]
public partial record Value4(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value4"/> instance contains the value 4.
/// </summary>
public record Value4Validator : ICborValidator<Value4>
{
    /// <summary>
    /// Validates that the input value equals 4.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 4; otherwise false.</returns>
    public bool Validate(Value4 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 4;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 5.
/// </summary>
/// <param name="Value">The integer value, validated to equal 5.</param>
[CborSerializable]
public partial record Value5(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value5"/> instance contains the value 5.
/// </summary>
public record Value5Validator : ICborValidator<Value5>
{
    /// <summary>
    /// Validates that the input value equals 5.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 5; otherwise false.</returns>
    public bool Validate(Value5 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 5;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 6.
/// </summary>
/// <param name="Value">The integer value, validated to equal 6.</param>
[CborSerializable]
public partial record Value6(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value6"/> instance contains the value 6.
/// </summary>
public record Value6Validator : ICborValidator<Value6>
{
    /// <summary>
    /// Validates that the input value equals 6.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 6; otherwise false.</returns>
    public bool Validate(Value6 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 6;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 7.
/// </summary>
/// <param name="Value">The integer value, validated to equal 7.</param>
[CborSerializable]
public partial record Value7(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value7"/> instance contains the value 7.
/// </summary>
public record Value7Validator : ICborValidator<Value7>
{
    /// <summary>
    /// Validates that the input value equals 7.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 7; otherwise false.</returns>
    public bool Validate(Value7 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 7;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 8.
/// </summary>
/// <param name="Value">The integer value, validated to equal 8.</param>
[CborSerializable]
public partial record Value8(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value8"/> instance contains the value 8.
/// </summary>
public record Value8Validator : ICborValidator<Value8>
{
    /// <summary>
    /// Validates that the input value equals 8.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 8; otherwise false.</returns>
    public bool Validate(Value8 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 8;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 9.
/// </summary>
/// <param name="Value">The integer value, validated to equal 9.</param>
[CborSerializable]
public partial record Value9(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value9"/> instance contains the value 9.
/// </summary>
public record Value9Validator : ICborValidator<Value9>
{
    /// <summary>
    /// Validates that the input value equals 9.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 9; otherwise false.</returns>
    public bool Validate(Value9 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 9;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 10.
/// </summary>
/// <param name="Value">The integer value, validated to equal 10.</param>
[CborSerializable]
public partial record Value10(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value10"/> instance contains the value 10.
/// </summary>
public record Value10Validator : ICborValidator<Value10>
{
    /// <summary>
    /// Validates that the input value equals 10.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 10; otherwise false.</returns>
    public bool Validate(Value10 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 10;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 11.
/// </summary>
/// <param name="Value">The integer value, validated to equal 11.</param>
[CborSerializable]
public partial record Value11(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value11"/> instance contains the value 11.
/// </summary>
public record Value11Validator : ICborValidator<Value11>
{
    /// <summary>
    /// Validates that the input value equals 11.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 11; otherwise false.</returns>
    public bool Validate(Value11 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 11;
    }
}

/// <summary>
/// A CBOR integer that must have the exact value 12.
/// </summary>
/// <param name="Value">The integer value, validated to equal 12.</param>
[CborSerializable]
public partial record Value12(int Value) : CborBase;

/// <summary>
/// Validator ensuring a <see cref="Value12"/> instance contains the value 12.
/// </summary>
public record Value12Validator : ICborValidator<Value12>
{
    /// <summary>
    /// Validates that the input value equals 12.
    /// </summary>
    /// <param name="input">The value to validate.</param>
    /// <returns>True if the value is 12; otherwise false.</returns>
    public bool Validate(Value12 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Value == 12;
    }
}
