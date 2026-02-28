using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a POSIX timestamp used in Cardano time-related operations.
/// </summary>
/// <param name="Value">The POSIX time value in milliseconds since epoch.</param>
[CborSerializable]
public partial record PosixTime(ulong Value) : CborBase;
