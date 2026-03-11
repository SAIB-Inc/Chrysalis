using Chrysalis.Codec.Types.Cardano.Core.Header;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Header;

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
    public static ulong MajorProtocolVersion(this ProtocolVersion self) => self.Major;
}
