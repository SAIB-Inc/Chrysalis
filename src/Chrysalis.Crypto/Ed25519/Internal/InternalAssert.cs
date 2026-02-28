namespace Chrysalis.Crypto.Internal;

/// <summary>
/// Internal assertion helper for cryptographic operations.
/// </summary>
internal static class InternalAssert
{
    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if the condition is false.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="message">The message to include in the exception.</param>
    internal static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("An assertion in Chrysalis.Crypto failed " + message);
    }
}
