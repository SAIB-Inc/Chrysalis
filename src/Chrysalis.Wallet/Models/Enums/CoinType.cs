namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the coin type used in BIP-44 derivation paths.
/// </summary>
public enum CoinType
{
    /// <summary>
    /// No coin type specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Represents the 'Ada' coin type. 1815 was chosen as it is the year of birth of Ada Lovelace.
    /// </summary>
    Ada = 1815
}
