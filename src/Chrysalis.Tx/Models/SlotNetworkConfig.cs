using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Configuration for converting between slot numbers and time for a specific Cardano network.
/// </summary>
/// <param name="ZeroTime">The Unix timestamp (milliseconds) of the genesis block.</param>
/// <param name="ZeroSlot">The slot number of the genesis block.</param>
/// <param name="SlotLength">The slot length in milliseconds.</param>
public record SlotNetworkConfig(long ZeroTime, long ZeroSlot, int SlotLength)
{
    /// <summary>Mainnet Shelley era slot configuration.</summary>
    public static readonly SlotNetworkConfig Mainnet = new(1596059091000, 4492800, 1000);

    /// <summary>Preprod testnet slot configuration.</summary>
    public static readonly SlotNetworkConfig Preprod = new(1655769600000, 0, 1000);

    /// <summary>Preview testnet slot configuration.</summary>
    public static readonly SlotNetworkConfig Preview = new(1666656000000, 0, 1000);

    /// <summary>Gets the default slot config for a given network type.</summary>
    public static SlotNetworkConfig FromNetworkType(NetworkType networkType) =>
        networkType == NetworkType.Mainnet ? Mainnet : Preprod;
}
