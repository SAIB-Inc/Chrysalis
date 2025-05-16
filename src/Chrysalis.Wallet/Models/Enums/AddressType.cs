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
    Delegation,
    ScriptDelegation
}