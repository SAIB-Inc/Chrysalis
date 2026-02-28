using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

/// <summary>
/// Extension methods for <see cref="ProtocolVersion"/> to access version fields.
/// </summary>
public static class ProtocolVersionExtensions
{
    /// <summary>
    /// Gets the major protocol version number.
    /// </summary>
    /// <param name="self">The protocol version instance.</param>
    /// <returns>The major protocol version.</returns>
    public static int MajorProtocolVersion(this ProtocolVersion self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.MajorProtocolVersion;
    }

    /// <summary>
    /// Gets the sequence number of the protocol version.
    /// </summary>
    /// <param name="self">The protocol version instance.</param>
    /// <returns>The sequence number.</returns>
    public static ulong SequenceNumber(this ProtocolVersion self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.SequenceNumber;
    }
}
