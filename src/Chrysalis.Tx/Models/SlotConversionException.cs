namespace Chrysalis.Tx.Models;

/// <summary>
/// A slot or a time could not be converted: the network has no <see cref="SlotNetworkConfig"/>, or the value is outside it.
/// </summary>
public sealed class SlotConversionException : InvalidOperationException
{
    /// <summary>Creates an exception without a message.</summary>
    public SlotConversionException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What could not be converted, and why.</param>
    public SlotConversionException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and a cause.</summary>
    /// <param name="message">What could not be converted, and why.</param>
    /// <param name="innerException">The cause.</param>
    public SlotConversionException(string message, Exception innerException) : base(message, innerException) { }
}
