using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Wallet.Utils;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Tx.Test.Fees;

internal static class CliUtxo
{
    // Fee pricing and the builders' byte tests need outrefs, values and script bytes, not the resolved output's datum.
    internal static ResolvedInput[] Read(CliVectors vectors, string path)
    {
        using JsonDocument document = JsonDocument.Parse(vectors.Bytes(path));
        return [.. document.RootElement.EnumerateObject().Select(entry =>
        {
            string[] outref = entry.Name.Split('#');
            TransactionInput input = TransactionInput.Create(Convert.FromHexString(outref[0]), ulong.Parse(outref[1], CultureInfo.InvariantCulture));
            Address address = new(Bech32Util.Decode(CliVectors.Text(entry.Value, "address")).data);
            CborEncodedValue? script = entry.Value.GetProperty("referenceScript") is { ValueKind: JsonValueKind.Object } reference
                ? ReadScript(reference.GetProperty("script")) : null;
            return new ResolvedInput(input, PostAlonzoTransactionOutput.Create(address, ReadValue(entry.Value.GetProperty("value")), scriptRef: script));
        })];
    }

    // Policies, then asset names, in the ledger's byte order (ordinal on their lowercase hex), which F03's capture does not keep:
    // cardano-ledger 95b369053f, Mary/Value.hs:108-115 (AssetName derives Ord).
    private static IValue ReadValue(JsonElement value) => ProviderValues.Create(CliVectors.Number(value, "lovelace"), value.EnumerateObject()
        .Where(policy => policy.Name != "lovelace").OrderBy(policy => policy.Name, StringComparer.Ordinal)
        .SelectMany(policy => policy.Value.EnumerateObject().OrderBy(asset => asset.Name, StringComparer.Ordinal)
            .Select(asset => ((ReadOnlyMemory<byte>)Convert.FromHexString(policy.Name), (ReadOnlyMemory<byte>)Convert.FromHexString(asset.Name), asset.Value.GetUInt64()))),
        "cardano-cli", "utxo");

    private static CborEncodedValue ReadScript(JsonElement script)
    {
        string kind = CliVectors.Text(script, "type");
        int language = kind switch
        {
            "SimpleScript" => 0,
            "PlutusScriptV1" => 1,
            "PlutusScriptV2" => 2,
            "PlutusScriptV3" => 3,
            _ => throw new InvalidDataException($"Unexpected script type: {kind}")
        };
        // The Plutus envelope has an extra outer byte string. Its content is the inner wire item.
        byte[] envelope = Convert.FromHexString(CliVectors.Text(script, "cborHex"));
        CborReader reader = new(envelope);
        ReadOnlySpan<byte> payload = language == 0 ? envelope : reader.ReadByteString();
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        writer.WriteBeginArray(2);
        writer.WriteInt32(language);
        if (language == 0)
        {
            buffer.Write(payload);
        }
        else
        {
            writer.WriteByteString(payload);
        }
        return new CborEncodedValue(buffer.WrittenMemory);
    }
}
