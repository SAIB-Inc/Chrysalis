using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Models.Addresses;

/// <summary>
/// Represents the header of a Cardano address, containing the address type and network type.
/// </summary>
/// <param name="Type">The address type.</param>
/// <param name="Network">The network type.</param>
public record AddressHeader(AddressType Type, NetworkType Network)
{
    /// <summary>
    /// Converts the address header to its byte representation.
    /// </summary>
    /// <returns>The header byte encoding the address type and network.</returns>
    public byte ToByte()
    {
        int networkNibble = Network == NetworkType.Mainnet ? 1 : 0;
        return (byte)(((byte)Type << 4) | networkNibble);
    }

    /// <summary>
    /// Creates an AddressHeader from a header byte.
    /// </summary>
    /// <param name="headerByte">The header byte to parse.</param>
    /// <returns>An AddressHeader instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The type or network nibble is not one of CIP-19's.</exception>
    public static AddressHeader FromByte(byte headerByte) => Address.GetAddressHeader(headerByte);

    /// <summary>
    /// Gets the Bech32 prefix for this address header (e.g., "addr", "addr_test", "stake", "stake_test").
    /// </summary>
    public string Prefix
    {
        get
        {
            string prefixCore = Type is AddressType.Delegation or AddressType.ScriptDelegation ? "stake" : "addr";
            string networkSuffix = Network == NetworkType.Mainnet ? string.Empty : "_test";
            return prefixCore + networkSuffix;
        }
    }
}
