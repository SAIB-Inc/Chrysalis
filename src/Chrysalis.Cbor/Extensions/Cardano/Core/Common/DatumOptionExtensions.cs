using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="DatumOption"/> to access option tag and data.
/// </summary>
public static class DatumOptionExtensions
{
    /// <summary>
    /// Gets the option tag indicating the datum type (hash or inline).
    /// </summary>
    /// <param name="self">The datum option instance.</param>
    /// <returns>The option tag value.</returns>
    public static int Option(this DatumOption self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            DatumHashOption datumHashOption => datumHashOption.Option,
            InlineDatumOption inlineDatumOption => inlineDatumOption.Option,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the inline datum data bytes, or empty if the datum is a hash reference.
    /// </summary>
    /// <param name="self">The datum option instance.</param>
    /// <returns>The datum data bytes.</returns>
    public static ReadOnlyMemory<byte> Data(this DatumOption self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            InlineDatumOption inlineDatumOption => inlineDatumOption.Data.Value,
            _ => ReadOnlyMemory<byte>.Empty
        };
    }
}
