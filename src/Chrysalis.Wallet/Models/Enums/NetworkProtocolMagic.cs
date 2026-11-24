namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// The network magic of each public Cardano network.
/// </summary>
public enum NetworkProtocolMagic
{
    /// <summary>
    /// No protocol magic specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Cardano pre-production test network (preprod).
    /// </summary>
    Preprod = 1,

    /// <summary>
    /// The Cardano preview test network.
    /// </summary>
    Preview = 2,

    /// <summary>
    /// The Cardano mainnet.
    /// </summary>
    Mainnet = 764824073
}
