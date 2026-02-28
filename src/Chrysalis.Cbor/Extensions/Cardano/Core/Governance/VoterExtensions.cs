using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="Voter"/> to access voter properties.
/// </summary>
public static class VoterExtensions
{
    /// <summary>
    /// Gets the voter type tag.
    /// </summary>
    /// <param name="self">The voter instance.</param>
    /// <returns>The type tag value.</returns>
    public static int Tag(this Voter self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Tag;
    }

    /// <summary>
    /// Gets the voter credential hash.
    /// </summary>
    /// <param name="self">The voter instance.</param>
    /// <returns>The hash bytes.</returns>
    public static byte[] Hash(this Voter self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Hash;
    }
}
