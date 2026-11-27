using System.Globalization;
using Chrysalis.Codec.Serialization;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

internal static class ProtocolUpdateWire
{
    internal static ProtocolParamUpdate Read(ReadOnlyMemory<byte> data, out int consumed)
    {
        (bool babbage, bool _, int expectedConsumed) = ScanMap(data, nameof(ProtocolParamUpdate), asValidation: false);
        if (babbage) { _ = BabbageProtocolParamUpdate.Read(data, out consumed); }
        else { _ = ConwayProtocolParamUpdate.Read(data, out consumed); }
        if (consumed != expectedConsumed || consumed <= 0)
        {
            throw CborError.Decode(data, 0, nameof(ProtocolParamUpdate), "complete protocol parameter update map");
        }
        return new ProtocolParamUpdate(babbage ? (byte)2 : (byte)1, data[..consumed]);
    }

    internal static void ValidateConstructorBytes(ReadOnlyMemory<byte> raw, bool isBabbage, string unionPath)
    {
        (bool hasBabbage, bool hasConway, int itemLength) = ScanMap(raw, unionPath, asValidation: true);
        if (itemLength != raw.Length) { throw CborError.Validation(itemLength, unionPath, "complete protocol parameter update map", "trailing bytes"); }
        if ((isBabbage && hasConway) || (!isBabbage && hasBabbage))
        {
            throw CborError.Validation(-1, unionPath, isBabbage ? "Babbage protocol parameter update keys" : "Conway protocol parameter update keys", "opposite-era update keys");
        }
    }

    private static (bool HasBabbage, bool HasConway, int ItemLength) ScanMap(ReadOnlyMemory<byte> data, string unionPath, bool asValidation)
    {
        if (data.IsEmpty || (data.Span[0] >> 5) != 5) { throw Fail(data, 0, unionPath, "map", CborError.Actual(data, 0), asValidation); }
        try
        {
            // Common keys have an identical representation. Babbage-only and Conway-only
            // fields select their schema; a mixture is not a known update in either era.
            CborReader reader = new(data.Span);
            reader.ReadBeginMap();
            int remaining = reader.ReadSize();
            bool isIndefinite = remaining < 0;
            bool babbage = false;
            bool conway = false;
            while (remaining > 0 || (isIndefinite && !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF))
            {
                int offset = data.Length - reader.Buffer.Length;
                if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) is not (0 or 1)) { throw Fail(data, offset, unionPath, "integer", CborError.Actual(data, offset), asValidation); }
                int key = reader.ReadInt32();
                babbage |= key is 12 or 13 or 14 or 15;
                conway |= key is >= 25 and <= 33;
                if (babbage && conway) { throw Fail(data, offset, unionPath, "one era's update fields", key.ToString(CultureInfo.InvariantCulture), asValidation); }

                int valOffset = data.Length - reader.Buffer.Length;
                if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xFF) { throw Fail(data, valOffset, unionPath, "map value", CborError.Actual(data, valOffset), asValidation); }
                reader.SkipDataItem();
                remaining -= remaining > 0 ? 1 : 0;
            }
            if (isIndefinite)
            {
                int breakOffset = data.Length - reader.Buffer.Length;
                if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xFF) { throw Fail(data, breakOffset, unionPath, "end of indefinite map", "end of input", asValidation); }
                _ = reader.ReadDataItem();
            }
            return (babbage, conway, data.Length - reader.Buffer.Length);
        }
        catch (CborException error)
        {
            throw asValidation
                ? CborError.Validation(Math.Max(0, error.Offset), unionPath, "protocol parameter update map", error.Message, error)
                : CborError.Wrap(error, data, unionPath, "registered union shape");
        }
    }

    private static Exception Fail(ReadOnlyMemory<byte> data, int offset, string unionPath, string expected, string actual, bool asValidation) =>
        asValidation ? CborError.Validation(offset, unionPath, expected, actual) : CborError.Decode(data, offset, unionPath, expected, actual);
}
