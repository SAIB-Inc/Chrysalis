using Chrysalis.Wallet.Addresses;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Extensions;

public static class AddressExtensions
{
    public static byte[]? GetPkh(this Address self)
    {
        byte[] addressBytes = self.ToBytes();

        if (addressBytes == null || addressBytes.Length < 2)
            return null;

        AddressHeader header = Address.GetAddressHeader(addressBytes[0]);

        // Helper method to extract the pkh from addressBytes.
        byte[]? ExtractPkh(int offset, int length)
        {
            if (addressBytes.Length < offset + length) return null;
            byte[] pkh = new byte[length];
            Buffer.BlockCopy(addressBytes, offset, pkh, 0, length);
            return pkh;
        }

        // Determine pkh based on address type.
        return header.Type switch
        {
            // base key-key
            AddressType.BasePayment or AddressType.BaseWithScriptDelegation or AddressType.BaseWithPointerDelegation or AddressType.EnterprisePayment => ExtractPkh(1, 28),
            // base script-key
            AddressType.ScriptPayment or AddressType.ScriptWithScriptDelegation or AddressType.ScriptWithPointerDelegation or AddressType.EnterpriseScriptPayment => null,
            _ => null,
        };
    }

    public static byte[]? GetSkh(this Address self)
    {
        byte[] addressBytes = self.ToBytes();

        if (addressBytes == null || addressBytes.Length < 57)
            return null;

        AddressHeader header = Address.GetAddressHeader(addressBytes[0]);

        // Helper method to extract the pkh from addressBytes.
        byte[]? ExtractSkh(int offset, int length)
        {
            byte[] skh = new byte[length];
            Buffer.BlockCopy(addressBytes, offset, skh, 0, length);
            return skh;
        }

        // Determine pkh based on address type.
        return header.Type switch
        {
            // base key-key
            AddressType.BasePayment or AddressType.BaseWithScriptDelegation => ExtractSkh(1 + 28, 28),
            // base script-key
            AddressType.ScriptPayment or AddressType.ScriptWithScriptDelegation or AddressType.ScriptWithPointerDelegation or AddressType.EnterpriseScriptPayment => null,
            _ => null,
        };
    }

    public static string GetPrefix(this Address self)
    {
        byte[] addressBytes = self.ToBytes();
        AddressHeader header = Address.GetAddressHeader(addressBytes[0]);
        return header.GetPrefix();
    }
}