using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Utility for converting between slot numbers and time across Cardano networks.
/// </summary>
public static class SlotUtil
{
    /// <summary>
    /// Gets the slot network configuration for Cardano mainnet.
    /// </summary>
    public static SlotNetworkConfig Mainnet { get; } = new SlotNetworkConfig(1596059091000L, 4492800L, 1000);

    /// <summary>
    /// Gets the slot network configuration for Cardano preprod.
    /// </summary>
    public static SlotNetworkConfig Preprod { get; } = new SlotNetworkConfig(1655769600000L, 86400L, 1000);

    /// <summary>
    /// Gets the slot network configuration for Cardano preview.
    /// </summary>
    public static SlotNetworkConfig Preview { get; } = new SlotNetworkConfig(1666656000000L, 0L, 1000);

    /// <summary>
    /// Gets the slot network configuration for the specified network type.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <returns>The slot network configuration.</returns>
    public static SlotNetworkConfig GetSlotNetworkConfig(NetworkType networkType)
    {
        return networkType switch
        {
            NetworkType.Mainnet => Mainnet,
            NetworkType.Preprod => Preprod,
            NetworkType.Preview => Preview,
            NetworkType.Testnet => throw new NotImplementedException(),
            NetworkType.Unknown => throw new NotImplementedException(),
            _ => new SlotNetworkConfig(0, 0, 0),
        };
    }

    /// <summary>
    /// Converts a Unix timestamp (seconds) to a slot number.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="unixTimeSeconds">The Unix timestamp in seconds.</param>
    /// <returns>The corresponding slot number.</returns>
    public static long GetSlotFromUnixTime(SlotNetworkConfig config, long unixTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(config);
        return unixTimeSeconds - (config.ZeroTime / 1000) + config.ZeroSlot;
    }

    /// <summary>
    /// Converts a UTC DateTime to a slot number.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="utcTime">The UTC time to convert.</param>
    /// <returns>The corresponding slot number.</returns>
    public static long GetSlotFromUTCTime(SlotNetworkConfig config, DateTime utcTime)
    {
        ArgumentNullException.ThrowIfNull(config);
        long unixTimeSeconds = (long)(utcTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        return GetSlotFromUnixTime(config, unixTimeSeconds);
    }

    /// <summary>
    /// Converts a slot number to a POSIX timestamp in seconds.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="slot">The slot number.</param>
    /// <returns>The corresponding POSIX timestamp in seconds.</returns>
    public static long GetPosixTimeSecondsFromSlot(SlotNetworkConfig config, long slot)
    {
        ArgumentNullException.ThrowIfNull(config);
        return (config.ZeroTime / 1000) + (slot - config.ZeroSlot);
    }

    /// <summary>
    /// Converts a slot number to a UTC DateTime.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="slot">The slot number.</param>
    /// <returns>The corresponding UTC DateTime.</returns>
    public static DateTime GetUTCTimeFromSlot(SlotNetworkConfig config, long slot)
    {
        ArgumentNullException.ThrowIfNull(config);
        long posixTimeSecondsFromSlot = GetPosixTimeSecondsFromSlot(config, slot);
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(posixTimeSecondsFromSlot);
    }
}
