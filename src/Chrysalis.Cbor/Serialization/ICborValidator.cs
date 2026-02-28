namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Defines a validation contract for a specific type.
/// </summary>
/// <typeparam name="T">The type to validate.</typeparam>
public interface ICborValidator<T>
{
    /// <summary>
    /// Validates the specified input.
    /// </summary>
    /// <param name="input">The input to validate.</param>
    /// <returns>True if the input is valid; otherwise, false.</returns>
    bool Validate(T input);
}
