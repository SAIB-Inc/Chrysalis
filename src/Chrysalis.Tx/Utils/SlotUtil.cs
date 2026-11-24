// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Utility for converting between slot numbers and time across Cardano networks.
/// </summary>
public static class SlotUtil
{
    /// <summary>
    /// Gets the slot network configuration for the specified network type.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <returns>The slot network configuration.</returns>
    /// <exception cref="SlotConversionException">The network has no built-in <see cref="SlotNetworkConfig"/>.</exception>
    public static SlotNetworkConfig GetSlotNetworkConfig(NetworkType networkType) =>
        SlotNetworkConfig.FromNetworkType(networkType);

    /// <summary>
    /// Converts a Unix timestamp (seconds) to a slot number.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="unixTimeSeconds">The Unix timestamp in seconds.</param>
    /// <returns>The corresponding slot number.</returns>
    /// <exception cref="SlotConversionException">The time is before the system start, or its slot is at or past <see cref="SlotNetworkConfig.EndSlot"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The seconds are outside the range of <see cref="DateTimeOffset"/>.</exception>
    public static long GetSlotFromUnixTime(SlotNetworkConfig config, long unixTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.ToSlot(DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds));
    }

    /// <summary>
    /// Converts a UTC DateTime to a slot number.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="utcTime">The UTC time to convert.</param>
    /// <returns>The corresponding slot number.</returns>
    /// <exception cref="SlotConversionException">The time is before the system start, or its slot is at or past <see cref="SlotNetworkConfig.EndSlot"/>.</exception>
    public static long GetSlotFromUTCTime(SlotNetworkConfig config, DateTime utcTime)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.ToSlot(new DateTimeOffset(utcTime.Ticks, TimeSpan.Zero));
    }

    /// <summary>
    /// Converts a slot number to a POSIX timestamp in seconds.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="slot">The slot number.</param>
    /// <returns>The corresponding POSIX timestamp in seconds.</returns>
    /// <exception cref="SlotConversionException">The slot is at or past <see cref="SlotNetworkConfig.EndSlot"/>, or its time is past the range of <see cref="DateTimeOffset"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The slot is negative.</exception>
    public static long GetPosixTimeSecondsFromSlot(SlotNetworkConfig config, long slot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        return config.ToTime((ulong)slot).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Converts a slot number to a UTC DateTime.
    /// </summary>
    /// <param name="config">The slot network configuration.</param>
    /// <param name="slot">The slot number.</param>
    /// <returns>The corresponding UTC DateTime.</returns>
    /// <exception cref="SlotConversionException">The slot is at or past <see cref="SlotNetworkConfig.EndSlot"/>, or its time is past the range of <see cref="DateTimeOffset"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The slot is negative.</exception>
    public static DateTime GetUTCTimeFromSlot(SlotNetworkConfig config, long slot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        return config.ToTime((ulong)slot).UtcDateTime;
    }
}
