using Chrysalis.Cardano.Core;
using CardanoSharpAddress = CardanoSharp.Wallet.Models.Addresses.Address;

namespace Chrysalis.Extensions;

public static class AddressExtension
{
    public static string ToBech32(this Address address)
        => new CardanoSharpAddress(address.Value).ToString();

    public static string? ToBech32(this byte[] address)
    {
        try
        {
            return new CardanoSharpAddress(address).ToString();
        }
        catch
        {
            return null;
        }
    }
}