namespace Chrysalis.Wallet.Models.Enums;

public enum AddressType
{
    BasePayment,            // PaymentKeyHash with StakeKeyHash
    ScriptPayment,          // ScriptHash with StakeKeyHash
    BaseWithScriptDelegation, // PaymentKeyHash with ScriptHash (delegation)
    ScriptWithScriptDelegation, // ScriptHash with ScriptHash (delegation)
    BaseWithPointerDelegation, // PaymentKeyHash with Pointer (delegation)
    ScriptWithPointerDelegation, // ScriptHash with Pointer (delegation)
    EnterprisePayment,      // PaymentKeyHash with no delegation (ø)
    EnterpriseScriptPayment,  // ScriptHash with no delegation (ø)
    StakeKey,               // Only the StakeKeyHash
    ScriptStakeKey          // ScriptHash with StakeKeyHash
}