using System.Buffers;

namespace Chrysalis.Codec.Serialization;

public interface ICborConverter
{
    void Write(IBufferWriter<byte> output, IList<object?> values, CborOptions options);
    object? Read(ReadOnlyMemory<byte> data, CborOptions options);
}
