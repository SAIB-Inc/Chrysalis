// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
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
/// <remarks><paramref name="IsDefinite"/> is kept for <see cref="Cbor.CborWriter.EncodePlutusDataDefinite"/>; <see cref="Cbor.CborWriter.EncodePlutusData"/> always writes the node's form.</remarks>
public sealed record PlutusDataConstr(BigInteger Tag, ImmutableArray<PlutusData> Fields, bool IsDefinite = false) : PlutusData;

/// <summary>An association list of key-value pairs.</summary>
public sealed record PlutusDataMap(ImmutableArray<(PlutusData Key, PlutusData Value)> Entries) : PlutusData;

/// <summary>A list of data values.</summary>
/// <remarks><paramref name="IsDefinite"/> is kept for <see cref="Cbor.CborWriter.EncodePlutusDataDefinite"/>; <see cref="Cbor.CborWriter.EncodePlutusData"/> always writes the node's form.</remarks>
public sealed record PlutusDataList(ImmutableArray<PlutusData> Values, bool IsDefinite = false) : PlutusData;

/// <summary>An arbitrary-precision integer.</summary>
public sealed record PlutusDataInteger(BigInteger Value) : PlutusData;

/// <summary>A byte string.</summary>
public sealed record PlutusDataByteString(ReadOnlyMemory<byte> Value) : PlutusData;

/// <summary>Indexes a container's immediate children in walk order (list items, constr fields, or map key/value pairs).</summary>
internal static class DataItems
{
    internal static int Count(PlutusData data) => data switch
    {
        PlutusDataList list => list.Values.Length,
        PlutusDataConstr constr => constr.Fields.Length,
        PlutusDataMap map => map.Entries.Length * 2,
        _ => 0
    };

    internal static PlutusData At(PlutusData data, int index) => data switch
    {
        PlutusDataList list => list.Values[index],
        PlutusDataConstr constr => constr.Fields[index],
        PlutusDataMap map => (index & 1) == 0 ? map.Entries[index >> 1].Key : map.Entries[index >> 1].Value,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
