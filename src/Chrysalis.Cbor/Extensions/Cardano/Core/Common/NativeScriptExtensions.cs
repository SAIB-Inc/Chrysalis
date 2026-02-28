using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using CNativeScript = Chrysalis.Cbor.Types.Cardano.Core.Common.NativeScript;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="CNativeScript"/> to access script properties across types.
/// </summary>
public static class NativeScriptExtensions
{
    /// <summary>
    /// Gets the script type tag.
    /// </summary>
    /// <param name="self">The native script instance.</param>
    /// <returns>The type tag value.</returns>
    public static int Tag(this CNativeScript self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ScriptPubKey scriptPubKey => scriptPubKey.Tag,
            ScriptAll scriptAll => scriptAll.Tag,
            ScriptAny scriptAny => scriptAny.Tag,
            ScriptNOfK scriptNOfK => scriptNOfK.Tag,
            InvalidBefore invalidBefore => invalidBefore.Tag,
            InvalidHereafter invalidHereAfter => invalidHereAfter.Tag,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the address key hash for a ScriptPubKey script.
    /// </summary>
    /// <param name="self">The native script instance.</param>
    /// <returns>The address key hash bytes.</returns>
    public static byte[] AddressKeyHash(this CNativeScript self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ScriptPubKey scriptPubKey => scriptPubKey.AddrKeyHash,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the child scripts from a multi-script (All, Any, or NOfK).
    /// </summary>
    /// <param name="self">The native script instance.</param>
    /// <returns>The child scripts.</returns>
    public static IEnumerable<CNativeScript> Scripts(this CNativeScript self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ScriptAll scriptAll => scriptAll.Scripts switch
            {
                CborIndefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            ScriptAny scriptAny => scriptAny.Scripts switch
            {
                CborDefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            ScriptNOfK scriptNOfK => scriptNOfK.Scripts switch
            {
                CborDefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            _ => []
        };
    }

    /// <summary>
    /// Gets the required signature count for a ScriptNOfK script.
    /// </summary>
    /// <param name="self">The native script instance.</param>
    /// <returns>The required count N.</returns>
    public static int N(this CNativeScript self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ScriptNOfK scriptNOfK => scriptNOfK.N,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the slot number for time-lock scripts (InvalidBefore or InvalidHereafter).
    /// </summary>
    /// <param name="self">The native script instance.</param>
    /// <returns>The slot number.</returns>
    public static ulong Slot(this CNativeScript self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            InvalidBefore invalidBefore => invalidBefore.Slot,
            InvalidHereafter invalidHereAfter => invalidHereAfter.Slot,
            _ => throw new NotSupportedException()
        };
    }
}
