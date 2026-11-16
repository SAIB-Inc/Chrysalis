using Chrysalis.Codec.Serialization;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.Handshake;

// Handshake-only construction; never sort a decoded table before validation.
internal static class HandshakeVersionTables
{
    internal static VersionTable Normalize(VersionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table switch
        {
            N2CVersionTable n2c => new N2CVersionTable(Normalize(n2c.Value, static key => key.Value)),
            N2NVersionTable n2n => new N2NVersionTable(Normalize(n2n.Value, static key => key.Value)),
            _ => throw new ArgumentException("Unknown handshake version-table family.", nameof(table))
        };
    }

    private static Dictionary<TVersion, TData> Normalize<TVersion, TData>(Dictionary<TVersion, TData> entries, Func<TVersion, int> number) where TVersion : notnull
    {
        ArgumentNullException.ThrowIfNull(entries);
        Dictionary<TVersion, TData> sorted = [];
        HashSet<int> seen = [];
        foreach (KeyValuePair<TVersion, TData> entry in entries.OrderBy(entry => number(entry.Key)))
        {
            if (!seen.Add(number(entry.Key)))
            {
                throw new ArgumentException("A version table cannot contain duplicate wire version numbers.", nameof(entries));
            }
            sorted.Add(entry.Key, entry.Value);
        }
        return sorted;
    }

    // Generated map readers silently drop duplicate equal keys before their own
    // validator runs. Outer handshake validators can inspect the completed Raw map.
    internal static bool ValidateWire(VersionTable? table)
    {
        if (table is null)
        {
            return false;
        }
        if (table.Raw.IsEmpty)
        {
            return true;
        }
        CborReader reader = new(table.Raw.Span);
        reader.ReadBeginMap();
        int count = reader.ReadSize();
        if (count < 0)
        {
            return false;
        }
        int previous = -1;
        for (int i = 0; i < count; i++)
        {
            int version = reader.ReadInt32();
            if (version <= previous)
            {
                return false;
            }
            previous = version;
            reader.SkipDataItem();
        }
        return !reader.DataAvailable;
    }

    internal static bool Validate<TVersion, TData>(Dictionary<TVersion, TData>? entries, Func<TVersion, int> number) where TVersion : notnull
    {
        if (entries is null || IndefiniteStateTracker.IsIndefinite(entries))
        {
            return false;
        }
        HashSet<int> seen = [];
        int previous = -1;
        bool ascending = true;
        foreach (TVersion key in entries.Keys)
        {
            int version = number(key);
            if (!seen.Add(version))
            {
                throw new ArgumentException("A version table cannot contain duplicate wire version numbers.", nameof(entries));
            }
            ascending &= version > previous;
            previous = version;
        }
        return ascending;
    }
}
