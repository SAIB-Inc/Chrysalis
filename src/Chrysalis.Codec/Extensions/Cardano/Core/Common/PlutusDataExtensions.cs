using Chrysalis.Codec.Types.Cardano.Core.Common;
using CPlutusData = Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="CPlutusData"/> to access raw bytes.
/// </summary>
public static class PlutusDataExtensions
{
    /// <summary>
    /// Gets the raw CBOR bytes of the Plutus data.
    /// </summary>
    /// <param name="self">The Plutus data instance.</param>
    /// <returns>The raw bytes, or empty if not available.</returns>
    public static ReadOnlyMemory<byte> Raw(this CPlutusData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Raw;
    }
}

/// <summary>
/// Factory methods for <see cref="IPlutusBool"/>.
/// </summary>
public static class PlutusBool
{
    /// <summary>
    /// Creates a Plutus-compatible boolean value.
    /// Maps <c>true</c> to <see cref="PlutusTrue"/> (Constr 1) and
    /// <c>false</c> to <see cref="PlutusFalse"/> (Constr 0).
    /// </summary>
    public static IPlutusBool From(bool value) => value
        ? PlutusTrue.Create()
        : PlutusFalse.Create();
}
