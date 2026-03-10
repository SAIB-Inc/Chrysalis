namespace Chrysalis.Codec.V2.Serialization;

public interface ICborType
{
    ReadOnlyMemory<byte> Raw { get; }
    int ConstrIndex { get; }
    bool IsIndefinite { get; }
}
