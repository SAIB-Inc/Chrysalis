namespace Chrysalis.Wallet.Models.Enums;

public enum AddressType
{
    Base,
    ScriptPaymentWithDelegation,
    PaymentWithScriptDelegation,
    ScriptPaymentWithScriptDelegation,
    PaymentWithPointerDelegation,
    ScriptPaymentWithPointerDelegation,
    EnterprisePayment,
    EnterpriseScriptPayment,
    ScriptPayment = EnterpriseScriptPayment, // Alias for backward compatibility
    Delegation,
    ScriptDelegation
}