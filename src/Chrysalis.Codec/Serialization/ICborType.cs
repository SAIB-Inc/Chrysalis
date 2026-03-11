namespace Chrysalis.Codec.Serialization;

public interface ICborType
{
    ReadOnlyMemory<byte> Raw { get; }
    int ConstrIndex { get; }
    bool IsIndefinite { get; }
}
