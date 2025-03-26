using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Extensions;

public static class AddressTypeExtensions
{
    public static bool IsBase(this AddressType type) =>
        type is AddressType.BasePayment or AddressType.BaseWithScriptDelegation;

    public static bool IsPointer(this AddressType type) =>
        type is AddressType.BaseWithPointerDelegation or AddressType.ScriptWithPointerDelegation;

    public static bool IsStakeAddress(this AddressType type) =>
        type is AddressType.StakeKey or AddressType.ScriptStakeKey;

    public static bool IsRewardAddress(this AddressType type) =>
        type is AddressType.StakeKey;

    public static bool HasStakePart(this AddressType type) =>
        type.IsBase() || type.IsPointer() || type is AddressType.ScriptPayment or AddressType.ScriptWithScriptDelegation;
}