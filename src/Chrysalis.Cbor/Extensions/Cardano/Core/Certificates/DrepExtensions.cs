using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

/// <summary>
/// Extension methods for <see cref="DRep"/> to access tag and key hash.
/// </summary>
public static class DRepExtensions
{
    /// <summary>
    /// Gets the DRep type tag.
    /// </summary>
    /// <param name="self">The DRep instance.</param>
    /// <returns>The type tag value.</returns>
    public static int Tag(this DRep self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            DRepAddrKeyHash dRepAddrKeyHash => dRepAddrKeyHash.Tag,
            DRepScriptHash dRepScriptHash => dRepScriptHash.Tag,
            Abstain abstain => abstain.Tag,
            DRepNoConfidence dRepNoConfidence => dRepNoConfidence.Tag,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the key hash or script hash of the DRep, if applicable.
    /// </summary>
    /// <param name="self">The DRep instance.</param>
    /// <returns>The key or script hash bytes, or null for abstain/no-confidence.</returns>
    public static ReadOnlyMemory<byte>? KeyHash(this DRep self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            DRepAddrKeyHash dRepAddrKeyHash => dRepAddrKeyHash.AddrKeyHash,
            DRepScriptHash dRepScriptHash => dRepScriptHash.ScriptHash,
            _ => null
        };
    }
}
