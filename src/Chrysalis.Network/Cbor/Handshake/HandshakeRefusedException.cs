using System.Globalization;

namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>A peer refused the handshake with a decoded protocol reason rather than an accept reply.</summary>
public sealed class HandshakeRefusedException : IOException
{
    /// <summary>Initializes an exception without a peer-provided reason.</summary>
    public HandshakeRefusedException() { }

    /// <summary>Initializes an exception with a caller-provided message.</summary>
    /// <param name="message">The diagnostic message.</param>
    public HandshakeRefusedException(string? message) : base(message) { }

    /// <summary>Initializes an exception with a caller-provided message and cause.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public HandshakeRefusedException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Initializes an exception carrying the complete reason supplied by the peer.</summary>
    /// <param name="reason">The decoded peer refusal; never null for an SDK-thrown handshake refusal.</param>
    public HandshakeRefusedException(RefuseReason reason) : base(Describe(reason)) => Reason = reason;

    /// <summary>Gets the peer reason; null only for manually constructed exceptions without a protocol reason.</summary>
    public RefuseReason? Reason { get; }

    private static string Describe(RefuseReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return reason switch
        {
            VersionMismatch mismatch => "Handshake refused: no common version; peer supports "
                + string.Join(", ", mismatch.VersionNumbers.Select(value => value.ToString(CultureInfo.InvariantCulture))) + ".",
            HandshakeDecodeError error => "Handshake decode error for wire version " + error.VersionNumber.ToString(CultureInfo.InvariantCulture)
                + ": " + error.ErrorData,
            Refused refused => "Handshake refused for wire version " + refused.VersionNumber.ToString(CultureInfo.InvariantCulture)
                + ": " + refused.ErrorData,
            _ => "Handshake refused by the peer."
        };
    }
}
