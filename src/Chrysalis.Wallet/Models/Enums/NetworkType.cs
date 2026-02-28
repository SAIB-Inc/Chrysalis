namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the type of Cardano network.
/// </summary>
public enum NetworkType
{
    /// <summary>
    /// The Cardano testnet network.
    /// </summary>
    Testnet,

    /// <summary>
    /// The Cardano mainnet network.
    /// </summary>
    Mainnet,

    /// <summary>
    /// The Cardano preview test network.
    /// </summary>
    Preview,

    /// <summary>
    /// The Cardano pre-production test network.
    /// </summary>
    Preprod,

    /// <summary>
    /// An unrecognized network type.
    /// </summary>
    Unknown,
}
