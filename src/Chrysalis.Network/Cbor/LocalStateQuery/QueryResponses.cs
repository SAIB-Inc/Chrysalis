using System.Numerics;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Represents the response to a current era query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="CurrentEra">The numeric identifier of the current Cardano era.</param>
[CborSerializable]
public partial record CurrentEraQueryResponse(
    ulong CurrentEra
) : CborRecord;

/// <summary>
/// Represents the response to a UTxO-by-address query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Utxos">A map of transaction inputs to their corresponding transaction outputs.</param>
[CborSerializable]
[CborList]
public partial record UtxoByAddressResponse(
    [CborOrder(0)] Dictionary<TransactionInput, ITransactionOutput> Utxos
) : CborRecord;

/// <summary>
/// Represents the response to a current protocol parameters query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="ProtocolParams">The current protocol parameters of the Cardano network.</param>
[CborSerializable]
[CborList]
public partial record CurrentProtocolParamsResponse(
    [CborOrder(0)] ProtocolParams ProtocolParams
) : CborRecord;

/// <summary>
/// Represents a Cardano era identifier used in LocalStateQuery responses.
/// </summary>
/// <param name="Era">The numeric identifier of the era.</param>
[CborSerializable]
public partial record CurrentEra(ulong Era) : CborRecord;

/// <summary>
/// The answer to the system-start query: the UTC time of slot zero, in the node's three fields.
/// </summary>
/// <param name="Year">The year.</param>
/// <param name="DayOfYear">The day of the year, counted from 1.</param>
/// <param name="PicosecondsOfDay">The time of day in picoseconds.</param>
[CborSerializable]
[CborList]
public partial record SystemStartResponse(
    [CborOrder(0)] int Year,
    [CborOrder(1)] int DayOfYear,
    [CborOrder(2)] ulong PicosecondsOfDay
) : CborRecord
{
    /// <summary>The system start as a UTC time, cut to 100 nanoseconds; the three fields keep the node's exact value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the date and time fall outside the <see cref="DateTimeOffset"/> range.</exception>
    public DateTimeOffset Utc => new DateTimeOffset(Year, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(DayOfYear - 1).AddTicks((long)(PicosecondsOfDay / 100_000));
}

/// <summary>
/// The answer to the era-history query: one summary per era the node's ledger knows, oldest first.
/// </summary>
/// <param name="Summaries">The era summaries, in era order.</param>
[CborSerializable]
public partial record EraHistoryResponse(List<EraSummary> Summaries) : CborRecord;

/// <summary>
/// One era of the era history.
/// </summary>
/// <param name="Start">Where the era starts.</param>
/// <param name="End">Where the era ends, or null when the node knows no end.</param>
/// <param name="Parameters">The era's slot and epoch parameters.</param>
[CborSerializable]
[CborList]
[CborDefinite]
public partial record EraSummary(
    [CborOrder(0)] EraBound Start,
    [CborOrder(1)] EraBound? End,
    [CborOrder(2)] EraParams Parameters
) : CborRecord;

/// <summary>
/// The start or the end of an era. Node 11.2.0 and later send four fields; earlier nodes send three.
/// </summary>
/// <param name="Time">The time since the system start in picoseconds, as the node wrote it; read it with <see cref="TimePicoseconds"/>.</param>
/// <param name="Slot">The slot.</param>
/// <param name="Epoch">The epoch.</param>
/// <param name="NextPerasRound">The next Peras round, or null when the node sent three fields.</param>
[CborSerializable]
[CborList]
public partial record EraBound(
    [CborOrder(0)] IPlutusBigInt Time,
    [CborOrder(1)] ulong Slot,
    [CborOrder(2)] ulong Epoch,
    [CborOrder(3)] ulong? NextPerasRound
) : CborRecord
{
    /// <summary>The time since the system start in picoseconds; past 2^64 the node writes it as a bignum.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the node sent a negative time.</exception>
    public BigInteger TimePicoseconds => Time switch
    {
        PlutusUint64 small => small.Value,
        PlutusBigUint big => new BigInteger(big.Value.Span, isUnsigned: true, isBigEndian: true),
        PlutusInt { Value: >= 0 } i32 => i32.Value,
        PlutusInt64 { Value: >= 0 } i64 => i64.Value,
        _ => throw new InvalidOperationException("The node sent a negative time for an era bound.")
    };
}

/// <summary>
/// The parameters of one era.
/// </summary>
/// <param name="EpochSize">The number of slots in an epoch.</param>
/// <param name="SlotLengthMilliseconds">The length of a slot in milliseconds.</param>
/// <param name="SafeZone">How far past the ledger tip the era is known not to end.</param>
/// <param name="GenesisWindow">The Genesis window in slots.</param>
/// <param name="PerasRoundLength">The Peras round length in slots, or null when the era has no Peras.</param>
[CborSerializable]
[CborList]
public partial record EraParams(
    [CborOrder(0)] ulong EpochSize,
    [CborOrder(1)] ulong SlotLengthMilliseconds,
    [CborOrder(2)] EraSafeZone SafeZone,
    [CborOrder(3)] ulong GenesisWindow,
    [CborOrder(4)] ulong? PerasRoundLength
) : CborRecord;

/// <summary>
/// The safe zone of an era: <see cref="StandardSafeZone"/> or <see cref="IndefiniteSafeZone"/>.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record EraSafeZone : CborRecord;

/// <summary>
/// The era cannot end within this many slots after the ledger tip.
/// </summary>
/// <param name="Idx">The CBOR index, 0.</param>
/// <param name="Slots">The number of slots.</param>
/// <param name="SafeBeforeEpoch">An item earlier nodes used, kept as received; current nodes send [0].</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record StandardSafeZone(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] ulong Slots,
    [CborOrder(2)] CborAny SafeBeforeEpoch
) : EraSafeZone;

/// <summary>
/// The era does not end: the node has no later era to move to.
/// </summary>
/// <param name="Idx">The CBOR index, 1.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record IndefiniteSafeZone(
    [CborOrder(0)] int Idx
) : EraSafeZone;
