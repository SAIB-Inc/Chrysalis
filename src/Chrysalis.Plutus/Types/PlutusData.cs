using System.Collections.Immutable;
using System.Numerics;

namespace Chrysalis.Plutus.Types;

/// <summary>
/// PlutusData — the universal data encoding for Cardano smart contracts.
/// A recursive 5-variant type used for datums, redeemers, and script contexts.
/// Encoded as CBOR on-chain (spec Appendix B).
/// </summary>
public abstract record PlutusData;

/// <summary>Constructor application: a tag index plus a list of fields.</summary>
public sealed record PlutusDataConstr(BigInteger Tag, ImmutableArray<PlutusData> Fields, bool IsDefinite = false) : PlutusData;

/// <summary>An association list of key-value pairs.</summary>
public sealed record PlutusDataMap(ImmutableArray<(PlutusData Key, PlutusData Value)> Entries) : PlutusData;

/// <summary>A list of data values.</summary>
public sealed record PlutusDataList(ImmutableArray<PlutusData> Values, bool IsDefinite = false) : PlutusData;

/// <summary>An arbitrary-precision integer.</summary>
public sealed record PlutusDataInteger(BigInteger Value) : PlutusData;

/// <summary>A byte string.</summary>
public sealed record PlutusDataByteString(ReadOnlyMemory<byte> Value) : PlutusData;
