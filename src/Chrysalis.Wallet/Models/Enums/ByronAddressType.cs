namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the spending-data address type tag inside a Byron-era address payload.
/// </summary>
public enum ByronAddressType
{
    /// <summary>
    /// Public-key (verification key) Byron address (tag 0).
    /// </summary>
    PublicKey = 0,

    /// <summary>
    /// Legacy Byron script address tag (tag 1; never appeared on mainnet and rejected by cardano-ledger).
    /// </summary>
    Script = 1,

    /// <summary>
    /// Byron AVVM redeem address (tag 2).
    /// </summary>
    Redeem = 2
}
