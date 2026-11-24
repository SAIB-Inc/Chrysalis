// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Globalization;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Models;

/// <summary>
/// How slots map to time on one network: when slot 0 began, each era's slot length and, when known,
/// the first slot whose time is not yet fixed.
/// </summary>
public sealed class SlotNetworkConfig
{
    private readonly SlotEra[] _eras;

    // Each era's start in milliseconds after the system start, summed from the eras before it.
    private readonly Int128[] _starts;

    /// <summary>Mainnet: 20-second Byron slots, then 1-second slots from slot 4,492,800.</summary>
    public static readonly SlotNetworkConfig Mainnet = new(new DateTimeOffset(2017, 9, 23, 21, 44, 51, TimeSpan.Zero), [new(0, 20_000), new(4_492_800, 1_000)]);

    /// <summary>Preprod: 20-second Byron slots, then 1-second slots from slot 86,400.</summary>
    public static readonly SlotNetworkConfig Preprod = new(new DateTimeOffset(2022, 6, 1, 0, 0, 0, TimeSpan.Zero), [new(0, 20_000), new(86_400, 1_000)]);

    /// <summary>Preview: 1-second slots from slot 0.</summary>
    public static readonly SlotNetworkConfig Preview = new(new DateTimeOffset(2022, 10, 25, 0, 0, 0, TimeSpan.Zero), [new(0, 1_000)]);

    /// <summary>Creates a network's <see cref="SlotNetworkConfig"/>.</summary>
    /// <param name="systemStart">When slot 0 began.</param>
    /// <param name="eras">Each era's first slot and slot length, oldest first; the first era starts at slot 0.</param>
    /// <param name="endSlot">The first slot whose time is not yet fixed, or null when later slots keep the last era's length.</param>
    /// <exception cref="ArgumentException">The eras are empty, the first does not start at slot 0, or they are out of slot order.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A slot length is zero or longer than <see cref="uint.MaxValue"/> milliseconds, or the end slot is before the last era's first slot.</exception>
    public SlotNetworkConfig(DateTimeOffset systemStart, IReadOnlyList<SlotEra> eras, ulong? endSlot = null)
    {
        ArgumentNullException.ThrowIfNull(eras);
        _eras = [.. eras];
        _starts = new Int128[_eras.Length];
        if (_eras.Length == 0 || _eras[0].StartSlot != 0)
        {
            throw new ArgumentException("The first era must start at slot 0.", nameof(eras));
        }
        for (int i = 0; i < _eras.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfZero(_eras[i].SlotLengthMilliseconds, nameof(eras));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_eras[i].SlotLengthMilliseconds, uint.MaxValue, nameof(eras));
            if (i > 0 && _eras[i].StartSlot < _eras[i - 1].StartSlot)
            {
                throw new ArgumentException("Eras must be in slot order.", nameof(eras));
            }
            _starts[i] = i == 0 ? 0 : _starts[i - 1] + ((Int128)(_eras[i].StartSlot - _eras[i - 1].StartSlot) * _eras[i - 1].SlotLengthMilliseconds);
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(endSlot ?? ulong.MaxValue, _eras[^1].StartSlot, nameof(endSlot));
        SystemStart = systemStart;
        EndSlot = endSlot;
    }

    /// <summary>Gets when slot 0 began.</summary>
    public DateTimeOffset SystemStart { get; }

    /// <summary>Gets each era's first slot and slot length, oldest first.</summary>
    public IReadOnlyList<SlotEra> Eras => _eras;

    /// <summary>Gets the first slot whose time is not yet fixed, or null when there is no end.</summary>
    public ulong? EndSlot { get; }

    /// <summary>Gets the built-in <see cref="SlotNetworkConfig"/> of Mainnet, Preprod or Preview.</summary>
    /// <param name="networkType">The network.</param>
    /// <returns>The network's built-in <see cref="SlotNetworkConfig"/>.</returns>
    /// <exception cref="SlotConversionException">Any other network: it has none built in.</exception>
    public static SlotNetworkConfig FromNetworkType(NetworkType networkType) => networkType switch
    {
        NetworkType.Mainnet => Mainnet,
        NetworkType.Preprod => Preprod,
        NetworkType.Preview => Preview,
        NetworkType.Testnet or NetworkType.Unknown or _ => throw new SlotConversionException(string.Create(CultureInfo.InvariantCulture,
            $"There is no built-in SlotNetworkConfig for the {networkType} network; read it from the node or Ogmios, or pass your own."))
    };

    // The POSIX time of a slot's start in milliseconds, truncated, as the ledger gives it to a script (Plutus/TxInfo.hs slotToPOSIXTime).
    internal long ToPosixMilliseconds(ulong slot)
    {
        Int128 milliseconds = SystemStart.ToUnixTimeMilliseconds() + Offset(slot);
        return milliseconds <= long.MaxValue ? (long)milliseconds : throw OutOfRange(slot);
    }

    // The start of a slot to the tick, the inverse of ToSlot.
    internal DateTimeOffset ToTime(ulong slot)
    {
        Int128 ticks = SystemStart.UtcTicks + (Offset(slot) * TimeSpan.TicksPerMillisecond);
        return ticks <= DateTimeOffset.MaxValue.UtcTicks ? new DateTimeOffset((long)ticks, TimeSpan.Zero) : throw OutOfRange(slot);
    }

    // The slot that holds a time: the time rounded down to its slot's start.
    internal long ToSlot(DateTimeOffset time)
    {
        Int128 ticks = (Int128)time.UtcTicks - SystemStart.UtcTicks;
        if (ticks < 0)
        {
            throw new SlotConversionException(string.Create(CultureInfo.InvariantCulture, $"{time:O} is before this network's system start, {SystemStart:O}."));
        }
        int era = Array.FindLastIndex(_starts, start => start * TimeSpan.TicksPerMillisecond <= ticks);
        Int128 slot = _eras[era].StartSlot + ((ticks - (_starts[era] * TimeSpan.TicksPerMillisecond)) / ((Int128)_eras[era].SlotLengthMilliseconds * TimeSpan.TicksPerMillisecond));
        return slot < (EndSlot ?? ulong.MaxValue) && slot <= long.MaxValue ? (long)slot : throw OutOfRange(slot);
    }

    // Milliseconds from the system start to a slot's start.
    private Int128 Offset(ulong slot)
    {
        int era = EraOf(slot);
        return _starts[era] + ((Int128)(slot - _eras[era].StartSlot) * _eras[era].SlotLengthMilliseconds);
    }

    // The last era starting at or before the slot, so empty eras at one slot are skipped.
    private int EraOf(ulong slot) => EndSlot <= slot ? throw OutOfRange(slot) : Array.FindLastIndex(_eras, era => era.StartSlot <= slot);

    private SlotConversionException OutOfRange(Int128 slot) => new(EndSlot is { } end && slot >= end
        ? string.Create(CultureInfo.InvariantCulture, $"Slot {slot} is at or past slot {end}, where this network's slot times are not yet fixed.")
        : string.Create(CultureInfo.InvariantCulture, $"Slot {slot} is out of range."));
}

/// <summary>One era of a <see cref="SlotNetworkConfig"/>.</summary>
/// <param name="StartSlot">The era's first slot.</param>
/// <param name="SlotLengthMilliseconds">The length of its slots in milliseconds.</param>
public readonly record struct SlotEra(ulong StartSlot, ulong SlotLengthMilliseconds);
