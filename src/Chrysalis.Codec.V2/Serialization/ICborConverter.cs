using System.Buffers;

namespace Chrysalis.Codec.V2.Serialization;

public interface ICborConverter
{
    void Write(IBufferWriter<byte> output, IList<object?> values, CborOptions options);
    object? Read(ReadOnlyMemory<byte> data, CborOptions options);
}
