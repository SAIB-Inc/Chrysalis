using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using SimpleBase;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Byron;

/// <summary>
/// Extension methods for <see cref="ByronAddress"/> encoding and decoding.
/// </summary>
public static class ByronAddressExtensions
{
    /// <summary>
    /// Encodes a Byron address to its base58 string representation.
    /// </summary>
    /// <param name="self">The Byron address.</param>
    /// <returns>The base58-encoded address string.</returns>
    public static string ToBase58(this ByronAddress self)
    {
        ArgumentNullException.ThrowIfNull(self);
        byte[] cborBytes = CborSerializer.Serialize(self);
        return Base58.Bitcoin.Encode(cborBytes);
    }

    /// <summary>
    /// Decodes a base58 string to a <see cref="ByronAddress"/>.
    /// </summary>
    /// <param name="base58">The base58-encoded Byron address string.</param>
    /// <returns>The decoded Byron address.</returns>
    public static ByronAddress FromBase58(string base58)
    {
        ArgumentNullException.ThrowIfNull(base58);
        byte[] cborBytes = Base58.Bitcoin.Decode(base58);
        return CborSerializer.Deserialize<ByronAddress>(cborBytes);
    }
}
