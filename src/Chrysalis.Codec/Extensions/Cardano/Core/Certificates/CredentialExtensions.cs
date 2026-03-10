using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Certificates;

/// <summary>
/// Extension methods for <see cref="Credential"/> to access type and hash.
/// </summary>
public static class CredentialExtensions
{
    /// <summary>
    /// Gets the credential type (key hash or script hash).
    /// </summary>
    /// <param name="self">The credential instance.</param>
    /// <returns>The credential type value.</returns>
    public static int Type(this Credential self) => self.Type;

    /// <summary>
    /// Gets the credential hash bytes.
    /// </summary>
    /// <param name="self">The credential instance.</param>
    /// <returns>The hash bytes.</returns>
    public static ReadOnlyMemory<byte> Hash(this Credential self) => self.Hash;
}
