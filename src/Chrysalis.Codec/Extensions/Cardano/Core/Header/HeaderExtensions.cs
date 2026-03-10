using Chrysalis.Codec.Types.Cardano.Core.Header;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Header;

/// <summary>
/// Extension methods for <see cref="BlockHeader"/> to access header components.
/// </summary>
public static class HeaderExtensions
{
    /// <summary>
    /// Gets the header body from the block header.
    /// </summary>
    /// <param name="self">The block header instance.</param>
    /// <returns>The block header body.</returns>
    public static IBlockHeaderBody HeaderBody(this BlockHeader self)
    {
        return self.HeaderBody;
    }

    /// <summary>
    /// Gets the body signature from the block header.
    /// </summary>
    /// <param name="self">The block header instance.</param>
    /// <returns>The body signature bytes.</returns>
    public static ReadOnlyMemory<byte> BodySignature(this BlockHeader self)
    {
        return self.BodySignature;
    }
}
