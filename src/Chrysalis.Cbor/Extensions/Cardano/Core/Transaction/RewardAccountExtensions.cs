using Chrysalis.Cbor.Types.Cardano.Core.Certificates;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="RewardAccount"/> to access the underlying value.
/// </summary>
public static class RewardAccountExtensions
{
    /// <summary>
    /// Gets the reward account bytes.
    /// </summary>
    /// <param name="self">The reward account instance.</param>
    /// <returns>The reward account bytes.</returns>
    public static ReadOnlyMemory<byte> Value(this RewardAccount self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Value;
    }
}
