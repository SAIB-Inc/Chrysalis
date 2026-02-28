namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the protocol magic number used to identify Cardano networks.
/// </summary>
public enum NetworkProtocolMagic
{
    /// <summary>
    /// No protocol magic specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// The protocol magic for the Cardano testnet.
    /// </summary>
    Testnet = 2,

    /// <summary>
    /// The protocol magic for the Cardano mainnet.
    /// </summary>
    Mainnet = 764824073
}
