namespace Chrysalis.Wallet.Models;

/// <summary>
/// Represents Cardano network identification information.
/// </summary>
/// <param name="NetworkId">The network identifier.</param>
/// <param name="NetworkMagic">The network magic number used for protocol handshakes.</param>
public record NetworkInfo(int NetworkId, int NetworkMagic);
