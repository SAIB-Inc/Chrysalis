using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Wallet.Utils;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Tx.Test.Fees;

internal static class CliUtxo
{
    // Fee pricing needs outrefs and script bytes, not the resolved output's tokens or datum.
    internal static ResolvedInput[] ReadForReferenceScripts(CliVectors vectors, string path)
    {
        using JsonDocument document = JsonDocument.Parse(vectors.Bytes(path));
        return [.. document.RootElement.EnumerateObject().Select(entry =>
        {
            string[] outref = entry.Name.Split('#');
            TransactionInput input = TransactionInput.Create(Convert.FromHexString(outref[0]), ulong.Parse(outref[1], CultureInfo.InvariantCulture));
            Address address = new(Bech32Util.Decode(CliVectors.Text(entry.Value, "address")).data);
            ulong coin = CliVectors.Number(entry.Value.GetProperty("value"), "lovelace");
            CborEncodedValue? script = entry.Value.GetProperty("referenceScript") is { ValueKind: JsonValueKind.Object } reference
                ? ReadScript(reference.GetProperty("script")) : null;
            return new ResolvedInput(input, PostAlonzoTransactionOutput.Create(address, Lovelace.FromAmount(coin), scriptRef: script));
        })];
    }

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
