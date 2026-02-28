namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the Cardano era of an address.
/// </summary>
public enum AddressEra
{
    /// <summary>
    /// The Byron era address format.
    /// </summary>
    Byron,

    /// <summary>
    /// The Shelley era address format.
    /// </summary>
    Shelley,

    /// <summary>
    /// An unrecognized address era.
    /// </summary>
    Unknown
}
