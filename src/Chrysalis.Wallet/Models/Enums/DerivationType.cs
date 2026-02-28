namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the type of key derivation in BIP-32 hierarchical deterministic wallets.
/// </summary>
public enum DerivationType
{
    /// <summary>
    /// Hardened derivation, producing keys that cannot be derived from the parent public key.
    /// </summary>
    HARD,

    /// <summary>
    /// Soft derivation, producing keys that can be derived from the parent public key.
    /// </summary>
    SOFT
}
