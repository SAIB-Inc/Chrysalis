using System.Formats.Cbor;
using Chrysalis.Cbor.Types.Cardano.Core;

namespace Chrysalis.Network.Cli;

public enum Era
{
    Unknown = 0,
    Byron = 1,
    Shelley = 2,
    Allegra = 3,
    Mary = 4,
    Alonzo = 5,
    Babbage = 6,
    Conway = 7
}

public static class TestUtils
{
    public static Block? DeserializeBlockWithEra(byte[] blockCbor)
    {
        CborReader reader = new(blockCbor, CborConformanceMode.Lax);
        reader.ReadTag();
        reader = new(reader.ReadByteString(), CborConformanceMode.Lax);
        reader.ReadStartArray();
        Era era = (Era)reader.ReadInt32();
        ReadOnlyMemory<byte> blockBytes = reader.ReadEncodedValue(true);

        return era switch
        {
            Era.Shelley or Era.Allegra or Era.Mary or Era.Alonzo => AlonzoCompatibleBlock.Read(blockBytes),
            Era.Babbage => BabbageBlock.Read(blockBytes),
            Era.Conway => ConwayBlock.Read(blockBytes),
            _ => throw new NotSupportedException($"Unsupported era: {era}")
        };
    }
}