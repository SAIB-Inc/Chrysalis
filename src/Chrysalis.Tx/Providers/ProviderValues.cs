using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Tx.Extensions;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Tx.Providers;

/// <summary>Builds immutable ledger values from decoded provider amounts.</summary>
public static class ProviderValues
{
    /// <summary>Groups by policy, rejecting a repeated policy and asset-name pair.</summary>
    public static IValue Create(ulong lovelace, IEnumerable<(ReadOnlyMemory<byte> Policy, ReadOnlyMemory<byte> Name, ulong Quantity)> assets,
        string provider, string operation)
    {
        ArgumentNullException.ThrowIfNull(assets);
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy = [with(ReadOnlyMemoryComparer.Instance)];
        foreach ((ReadOnlyMemory<byte> policy, ReadOnlyMemory<byte> name, ulong quantity) in assets)
        {
            if (policy.Length != 28 || name.Length > 32) { throw ProviderHttp.Unusable(provider, operation, "invalid asset identifier"); }
            if (!byPolicy.TryGetValue(policy, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
            {
                bundle = [with(ReadOnlyMemoryComparer.Instance)];
                byPolicy.Add(policy, bundle);
            }
            if (!bundle.TryAdd(name, quantity)) { throw ProviderHttp.Unusable(provider, operation, "repeated asset"); }
        }
        if (byPolicy.Count == 0) { return Lovelace.Create(lovelace); }
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> output = [with(ReadOnlyMemoryComparer.Instance)];
        foreach ((ReadOnlyMemory<byte> policy, Dictionary<ReadOnlyMemory<byte>, ulong> bundle) in byPolicy)
        {
            output.Add(policy, TokenBundleOutput.Create(bundle));
        }
        return LovelaceWithMultiAsset.Create(lovelace, MultiAssetOutput.Create(output));
    }

    /// <summary>Converts a published decimal to an exact, reduced unsigned ledger ratio.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <exception cref="OverflowException">The reduced numerator or denominator does not fit in 64 bits.</exception>
    public static CborRationalNumber ExactRational(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Span<int> bits = stackalloc int[4];
        _ = decimal.GetBits(value, bits);
        BigInteger numerator = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        BigInteger denominator = BigInteger.Pow(10, (bits[3] >> 16) & 0xff);
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return CborRationalNumber.Create((ulong)(numerator / divisor), (ulong)(denominator / divisor));
    }

    /// <summary>Maps db-sync native script JSON to a ledger script.</summary>
    /// <param name="json">The native script JSON object.</param>
    /// <returns>The decoded ledger native script.</returns>
    /// <exception cref="FormatException">A key hash or numeric field has an invalid format.</exception>
    /// <exception cref="InvalidOperationException">A JSON value has the wrong kind or an unknown script type.</exception>
    /// <exception cref="KeyNotFoundException">A required JSON property is missing.</exception>
    /// <exception cref="OverflowException">An <c>atLeast</c> threshold exceeds <see cref="int.MaxValue"/>.</exception>
    public static INativeScript NativeScript(JsonElement json) => json.GetProperty("type").GetString() switch
    {
        "sig" => ScriptPubKey.Create(0, KeyHash(json.GetProperty("keyHash").GetString())),
        "all" => ScriptAll.Create(1, Children(json)),
        "any" => ScriptAny.Create(2, Children(json)),
        "atLeast" => ScriptNOfK.Create(3, checked((int)json.GetProperty("required").GetUInt32()), Children(json)),
        "after" => InvalidBefore.Create(4, json.GetProperty("slot").GetUInt64()),
        "before" => InvalidHereafter.Create(5, json.GetProperty("slot").GetUInt64()),
        _ => throw new InvalidOperationException("Unknown native script type.")
    };

    private static CborList<INativeScript> Children(JsonElement json) =>
        CborList.Create(json.GetProperty("scripts").EnumerateArray().Select(NativeScript));

    private static byte[] KeyHash(string? hash) => hash is { Length: 56 }
        ? Convert.FromHexString(hash) : throw new FormatException("Invalid native script key hash length.");

    /// <summary>Verifies that a decoded script matches the expected hash.</summary>
    public static IScript RequireScriptHash(IScript script, string expectedHash, string provider, string operation)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!string.Equals(script.HashHex(), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw ProviderHttp.Unusable(provider, operation, "script hash differs from the requested hash");
        }
        return script;
    }

    /// <summary>Extracts transaction metadata from CBOR transaction bytes without decoding the body.</summary>
    public static Metadata? TransactionMetadata(ReadOnlyMemory<byte> transaction) =>
        AuxiliaryData(transaction) is { } data ? CborSerializer.Deserialize<IAuxiliaryData>(data).Metadata() : null;

    private static ReadOnlyMemory<byte>? AuxiliaryData(ReadOnlyMemory<byte> tx)
    {
        CborReader reader = new(tx.Span);
        reader.ReadBeginArray();
        bool indefinite = tx.Span[0] == 0x9f;
        int count = reader.ReadSize() switch
        {
            _ when indefinite => -1,
            2 => 2,
            3 => 3,
            4 => 4,
            _ => throw new InvalidDataException("Invalid transaction array length.")
        };
        _ = reader.ReadDataItem();
        _ = reader.ReadDataItem();
        ReadOnlyMemory<byte>? auxiliary = null;
        bool ended = indefinite && !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xff;
        if (count != 2 && !ended)
        {
            if (reader.Buffer.IsEmpty) { throw new InvalidDataException("Truncated transaction array."); }
            bool validityFlag = reader.Buffer[0] is 0xf4 or 0xf5;
            if (validityFlag)
            {
                if (!indefinite && count != 4) { throw new InvalidDataException("A validity flag requires four transaction items."); }
                _ = reader.ReadDataItem();
            }
            else if (count == 4) { throw new InvalidDataException("Missing transaction validity flag."); }

            if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xff) { throw new InvalidDataException("Missing transaction auxiliary item."); }
            bool absent = reader.Buffer[0] == 0xf6;
            int start = tx.Length - reader.Buffer.Length;
            _ = reader.ReadDataItem();
            if (!absent) { auxiliary = tx[start..(tx.Length - reader.Buffer.Length)]; }
        }

        if (indefinite ? reader.Buffer.Length != 1 || reader.Buffer[0] != 0xff : !reader.Buffer.IsEmpty)
        {
            throw new InvalidDataException("Unterminated transaction array or trailing bytes.");
        }
        return auxiliary;
    }
}
