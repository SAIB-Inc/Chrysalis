namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the type of a Cardano credential.
/// </summary>
public enum CredentialType
{
    /// <summary>
    /// A credential derived from a verification key hash.
    /// </summary>
    KeyHash,

    /// <summary>
    /// A credential derived from a script hash.
    /// </summary>
    ScriptHash
}
