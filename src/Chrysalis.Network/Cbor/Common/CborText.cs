using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.Common;

/// <summary>Readable text for CBOR items that the node sends.</summary>
internal static class CborText
{
    private const string EraShapeError = "Expected an era as [index, name]";

    // Deeper than any ledger error, and the bound on Append's recursion (System.Text.Json's default MaxDepth).
    internal const int MaxDepth = 64;

    /// <summary>
    /// Writes one validated CBOR item in diagnostic notation (RFC 8949 §8); text strings are JSON-escaped via
    /// <see cref="JsonEncodedText"/> with <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> (escaping
    /// <c>"</c>, <c>\</c>, control characters, and any other code points that encoder forbids).
    /// </summary>
    /// <exception cref="CborException">Thrown when the item holds a value this text leaves out: a float, another
    /// simple value, a negative below -2^63 or indefinite-length text.</exception>
    internal static string Diagnostic(ReadOnlySpan<byte> item)
    {
        StringBuilder text = new();
        _ = Append(item, text);
        return text.ToString();
    }

    /// <summary>Reads an era mismatch, [[index, name], [index, name]], as text naming both eras.</summary>
    /// <exception cref="CborException">Thrown when the mismatch cannot be read.</exception>
    internal static string EraMismatch(ReadOnlySpan<byte> raw, string subject)
    {
        CborReader reader = new(raw);
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        string first = ReadEra(ref reader);
        return $"Era mismatch: the {subject} is for {first}, but the node's ledger is in {ReadEra(ref reader)}.";
    }

    // Each era is [index, name] (AcrossEras.hs:128-138).
    private static string ReadEra(ref CborReader reader)
    {
        reader.ReadBeginArray();
        ulong index = reader.ReadSize() == 2 ? reader.ReadUInt64() : throw reader.BuildException(EraShapeError);
        return $"{reader.ReadString() ?? throw reader.BuildException(EraShapeError)} ({index})";
    }

    // Writes the item at the start of data and returns its length in bytes.
    private static int Append(ReadOnlySpan<byte> data, StringBuilder text)
    {
        CborReader reader = new(data);
        switch (data[0] >> 5)
        {
            case 0:
                _ = text.Append(CultureInfo.InvariantCulture, $"{reader.ReadUInt64()}");
                break;
            case 1:
                _ = text.Append(CultureInfo.InvariantCulture, $"{reader.ReadInt64()}");
                break;
            case 2:
                _ = text.Append("h'").Append(Convert.ToHexStringLower(reader.ReadByteString())).Append('\'');
                break;
            case 3:
                if (data[0] == 0x7F) { throw reader.BuildException("Indefinite-length text strings are not shown"); }
                _ = text.Append('"').Append(JsonEncodedText.Encode(reader.ReadString()!, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value).Append('"');
                break;
            case 4 or 5:
                {
                    bool map = data[0] >= 0xA0;
                    if (map) { reader.ReadBeginMap(); } else { reader.ReadBeginArray(); }
                    long count = reader.ReadSize() * (map ? 2L : 1L);
                    int offset = data.Length - reader.Buffer.Length;
                    _ = text.Append(map ? '{' : '[').Append(count < 0 ? "_ " : "");
                    for (long index = 0; count < 0 ? data[offset] != 0xFF : index < count; index++)
                    {
                        _ = text.Append(index == 0 ? "" : map && index % 2 == 1 ? ": " : ", ");
                        offset += Append(data[offset..], text);
                    }
                    _ = text.Append(map ? '}' : ']');
                    return count < 0 ? offset + 1 : offset;
                }
            case 6:
                {
                    _ = reader.TryReadSemanticTag(out ulong tag);
                    int header = data.Length - reader.Buffer.Length;
                    _ = text.Append(CultureInfo.InvariantCulture, $"{tag}(");
                    int content = Append(data[header..], text);
                    _ = text.Append(')');
                    return header + content;
                }
            default:
                _ = text.Append(data[0] switch
                {
                    0xF4 => "false",
                    0xF5 => "true",
                    0xF6 => "null",
                    0xF7 => "undefined",
                    _ => throw reader.BuildException("Floats and other simple values are not shown"),
                });
                break;
        }
        return new CborReader(data).ReadDataItem().Length;
    }
}
