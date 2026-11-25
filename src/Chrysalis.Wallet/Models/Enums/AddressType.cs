namespace Chrysalis.Wallet.Models.Enums;

/// <summary>
/// Represents the type of a Cardano address.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "CIP-0019 4-bit address type nibbles (0..8, 14, 15) are mutually exclusive, not bit flags.")]
public enum AddressType
{
    /// <summary>
    /// Base address with payment and stake key hash credentials.
    /// </summary>
    Base = 0,

    /// <summary>
    /// Address with script payment credential and key hash delegation credential.
    /// </summary>
    ScriptPaymentWithDelegation = 1,

    /// <summary>
    /// Address with key hash payment credential and script delegation credential.
    /// </summary>
    PaymentWithScriptDelegation = 2,

    /// <summary>
    /// Address with script payment credential and script delegation credential.
    /// </summary>
    ScriptPaymentWithScriptDelegation = 3,

    /// <summary>
    /// Address with key hash payment credential and pointer delegation.
    /// </summary>
    PaymentWithPointerDelegation = 4,

    /// <summary>
    /// Address with script payment credential and pointer delegation.
    /// </summary>
    ScriptPaymentWithPointerDelegation = 5,

    /// <summary>
    /// Enterprise address with key hash payment credential only.
    /// </summary>
    EnterprisePayment = 6,

    /// <summary>
    /// Enterprise address with script payment credential only.
    /// </summary>
    EnterpriseScriptPayment = 7,

    /// <summary>
    /// Byron-era bootstrap address (CIP-0019 header nibble 1000).
    /// </summary>
    Byron = 8,

    /// <summary>
    /// Reward/stake delegation address with key hash credential.
    /// </summary>
    Delegation = 14,

    /// <summary>
    /// Reward/stake delegation address with script credential.
    /// </summary>
    ScriptDelegation = 15
}
