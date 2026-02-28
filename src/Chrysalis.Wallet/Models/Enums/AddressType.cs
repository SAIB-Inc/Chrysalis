namespace Chrysalis.Wallet.Models.Enums;

public enum AddressType
{
    Base = 0,
    ScriptPaymentWithDelegation = 1,
    PaymentWithScriptDelegation = 2,
    ScriptPaymentWithScriptDelegation = 3,
    PaymentWithPointerDelegation = 4,
    ScriptPaymentWithPointerDelegation = 5,
    EnterprisePayment = 6,
    EnterpriseScriptPayment = 7,
    Delegation = 14,
    ScriptDelegation = 15
}