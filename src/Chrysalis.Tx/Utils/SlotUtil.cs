using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Utils;

public static class SlotUtil
{
    public static SlotNetworkConfig Mainnet { get; set; } = new SlotNetworkConfig(1596059091000L, 4492800L, 1000);


    public static SlotNetworkConfig Preprod { get; set; } = new SlotNetworkConfig(1655769600000L, 86400L, 1000);


    public static SlotNetworkConfig Preview { get; set; } = new SlotNetworkConfig(1666656000000L, 0L, 1000);


    public static SlotNetworkConfig GetSlotNetworkConfig(NetworkType networkType)
    {
        return networkType switch
        {
            NetworkType.Mainnet => Mainnet,
            NetworkType.Preprod => Preprod,
            NetworkType.Preview => Preview,
            _ => new SlotNetworkConfig(0,0,0),
        };
    }

    public static long GetSlotFromUnixTime(SlotNetworkConfig config, long unixTimeSeconds)
    {
        return unixTimeSeconds - config.ZeroTime / 1000 + config.ZeroSlot;
    }

    public static long GetSlotFromUTCTime(SlotNetworkConfig config, DateTime utcTime)
    {
        long unixTimeSeconds = (long)(utcTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        return GetSlotFromUnixTime(config, unixTimeSeconds);
    }

    public static long GetPosixTimeSecondsFromSlot(SlotNetworkConfig config, long slot)
    {
        return config.ZeroTime / 1000 + (slot - config.ZeroSlot);
    }

    public static DateTime GetUTCTimeFromSlot(SlotNetworkConfig config, long slot)
    {
        long posixTimeSecondsFromSlot = GetPosixTimeSecondsFromSlot(config, slot);
        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(posixTimeSecondsFromSlot);
    }
}